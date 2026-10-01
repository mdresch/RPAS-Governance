using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>Stand-in petitioner authentication: identity comes from a request header.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string PetitionerHeader = "X-Test-Petitioner";
    public const string NoIdentityClaim = "__authenticated-without-petitioner-claim__";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PetitionerHeader, out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();
        if (value.ToString() != NoIdentityClaim)
        {
            claims.Add(new Claim("azp", value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class TestDatabase
{
    public static string NewPath() => Path.Combine(Path.GetTempPath(), $"rpas-test-{Guid.NewGuid():N}.db");

    public static string ConnectionString(string path) => $"Data Source={path};Default Timeout=30;Pooling=False";

    public static GovernanceDbContext NewContext(string path, bool withLawInterceptor = false)
    {
        var builder = new DbContextOptionsBuilder<GovernanceDbContext>().UseSqlite(ConnectionString(path));
        if (withLawInterceptor)
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(); // default: Enforced
            builder.AddInterceptors(new RpasLawEnforcementInterceptor(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RpasLawEnforcementInterceptor>.Instance, config));
        }
        return new GovernanceDbContext(builder.Options);
    }

    public static void Create(string path)
    {
        using var db = NewContext(path);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    public static void Delete(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(path + suffix); } catch { /* best effort */ }
        }
    }
}

/// <summary>
/// Hosts the real API with a SQLite file database. With useTestAuth == false the API runs exactly as
/// shipped with no authority configured, i.e. in its fail-closed state.
/// </summary>
public sealed class ApiFactory(bool useTestAuth, IDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public string DbPath { get; } = TestDatabase.NewPath();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Governance:SkipEfMigrations", "true");

        // petitioner-a and petitioner-b are full-content petitioners (they exercise the BusinessCase/ledger petitions).
        // Every other petitioner is hash-only by default, which is the fail-closed behaviour under test.
        builder.UseSetting("Governance:Petitioners:petitioner-a:ContentMode", "FullContent");
        builder.UseSetting("Governance:Petitioners:petitioner-b:ContentMode", "FullContent");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<GovernanceDbContext>>();
            // EF Core 9+ keeps AddDbContext's configuration lambda in a separate (internal) service type.
            foreach (var descriptor in services.Where(d =>
                         d.ServiceType.IsGenericType &&
                         d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal) &&
                         d.ServiceType.GetGenericArguments()[0] == typeof(GovernanceDbContext)).ToList())
            {
                services.Remove(descriptor);
            }
            services.AddDbContext<GovernanceDbContext>((sp, options) =>
            {
                options.UseSqlite(TestDatabase.ConnectionString(DbPath));
                options.AddInterceptors(sp.GetRequiredService<RpasLawEnforcementInterceptor>());
            });
        });

        if (useTestAuth)
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }
    }

    public HttpClient CreateClientFor(string? petitioner)
    {
        var client = CreateClient();
        if (petitioner is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.PetitionerHeader, petitioner);
        }
        return client;
    }

    public void EnsureDatabase()
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            TestDatabase.Delete(DbPath);
        }
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Api.Security;
using RPAS.Governance.Persistence.Data;

var builder = WebApplication.CreateBuilder(args);

// Add standard Aspire defaults (Health checks, OTel, Resilience)
builder.AddServiceDefaults();

// Petitioner authentication (AMD-2026-10-01-0003). Fails closed when no authority is configured.
builder.Services.AddRpasAuthentication(builder.Configuration);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Sovereign persistence with built-in Aspire health checks and pooling (same connection name as Adpa orchestrator)
builder.Services.AddSingleton<RpasLawEnforcementInterceptor>();
builder.Services.AddDbContext<GovernanceDbContext>((sp, options) =>
{
    // No credentials are stored in source (AMD-2026-10-01-0004). The connection string comes from the
    // environment, user-secrets or a secret store; under Aspire it is injected as "governanceDb".
    var connectionString = builder.Configuration.GetConnectionString("governance-ledger")
                           ?? builder.Configuration.GetConnectionString("governanceDb");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "No ledger connection string configured. Set ConnectionStrings:governance-ledger " +
            "(or ConnectionStrings:governanceDb) via environment variables, user-secrets or your secret store.");
    }
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<RpasLawEnforcementInterceptor>());
});

var app = builder.Build();

// Map standard Aspire endpoints (/health, /alive)
app.MapDefaultEndpoints();

if (!app.Configuration.GetValue("Governance:SkipEfMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var _db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
    try
    {
        _db.Database.Migrate();
    }
    catch
    {
        _db.Database.EnsureCreated();
    }
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireAuthorization();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program { }

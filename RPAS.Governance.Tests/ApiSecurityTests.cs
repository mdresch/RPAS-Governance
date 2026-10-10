using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>
/// AMD-2026-10-01-0001 / 0002 / 0003 exercised through the real HTTP pipeline.
/// </summary>
public sealed class ApiSecurityTests : IDisposable
{
    private readonly ApiFactory _authed = new(useTestAuth: true);
    private readonly ApiFactory _unconfigured = new(useTestAuth: false);

    public ApiSecurityTests()
    {
        _authed.EnsureDatabase();
        _unconfigured.EnsureDatabase();
    }

    public void Dispose()
    {
        _authed.Dispose();
        _unconfigured.Dispose();
    }

    private Guid SeedToken(string? petitionerId, string ritual = "MarkApproved", int ttlSeconds = 120)
    {
        using var scope = _authed.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        var token = new AuthorityToken(ritual, "entity-1", ttlSeconds, petitionerId);
        foreach (var p in RitualSeed.PathsFor(ritual))
        {
            token.AllowedPaths.Add(p);
        }
        db.AuthorityTokens.Add(token);
        db.SaveChanges();
        return token.Id;
    }

    private bool IsConsumed(Guid id)
    {
        using var scope = _authed.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        return db.AuthorityTokens.Single(t => t.Id == id).IsConsumed;
    }

    private static object Execute(Guid id, string path) => new { TokenId = id.ToString(), TargetPath = path };

    // ---- AMD-0003: authentication -------------------------------------------------------------

    [Theory]
    [InlineData("/Mutation/execute")]
    [InlineData("/Validation/validate")]
    [InlineData("/Preflight/check")]
    [InlineData("/Replay/replay")]
    public async Task WithoutConfiguredAuthentication_EveryEndpointFailsClosed(string route)
    {
        using var client = _unconfigured.CreateClientFor(petitioner: "petitioner-a"); // header is ignored: no auth configured
        var response = await client.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowDevTokens_AuthenticatesBearerPetitioner_InDevelopment()
    {
        using var devFactory = new ApiFactory(useTestAuth: false, new Dictionary<string, string?>
        {
            ["Authentication:AllowDevTokens"] = "true",
            ["Governance:Petitioners:sidpa:Scopes"] = "declare-intent",
        });
        devFactory.EnsureDatabase();

        using var client = devFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token-sidpa-service-principal");

        var response = await client.PostAsJsonAsync("/Tokens/issue", new
        {
            RitualType = "DeclareIntent",
            EntityId = "doc-dev-1",
            TtlSeconds = 300
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("/Mutation/execute")]
    [InlineData("/Validation/validate")]
    [InlineData("/Preflight/check")]
    [InlineData("/Replay/replay")]
    public async Task UnauthenticatedCaller_IsRejected(string route)
    {
        using var client = _authed.CreateClientFor(petitioner: null);
        var response = await client.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedCallerWithoutPetitionerClaim_IsForbidden()
    {
        var id = SeedToken("petitioner-a");
        using var client = _authed.CreateClientFor(TestAuthHandler.NoIdentityClaim);

        var response = await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(IsConsumed(id));
    }

    [Fact]
    public async Task IssuedToken_IsBoundToTheRequestingPetitioner()
    {
        using var client = _authed.CreateClientFor("petitioner-a");
        var petition = new
        {
            EntityType = "GovernanceLedgerEntry",
            EntityId = "e-1",
            Action = "Create",
            Payload = System.Text.Json.JsonDocument.Parse("{\"RitualType\":\"Create\"}").RootElement // exact casing: the controller deserializes case-sensitively
        };

        var response = await client.PostAsJsonAsync("/Validation/validate", petition);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var scope = _authed.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        var token = Assert.Single(db.AuthorityTokens.ToList());
        Assert.Equal("petitioner-a", token.PetitionerId);
        Assert.Contains(db.GovernanceLedgerEntries.ToList(), e => e.GovernorNotes?.Contains("Petitioner: petitioner-a") == true);
    }

    [Fact]
    public async Task TokenIssuedToAnotherPetitioner_IsForbidden_AndStaysUsable()
    {
        var id = SeedToken("petitioner-a");

        using var other = _authed.CreateClientFor("petitioner-b");
        var stolen = await other.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"));
        Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);
        Assert.False(IsConsumed(id));

        using var owner = _authed.CreateClientFor("petitioner-a");
        var legit = await owner.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"));
        Assert.Equal(HttpStatusCode.OK, legit.StatusCode);
    }

    [Fact]
    public async Task UnboundLegacyToken_IsNeverHonoured()
    {
        var id = SeedToken(petitionerId: null);
        using var client = _authed.CreateClientFor("petitioner-a");

        var response = await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(IsConsumed(id));
    }

    // ---- AMD-0001: replay and races -----------------------------------------------------------

    [Fact]
    public async Task Replay_IsRejected()
    {
        var id = SeedToken("petitioner-a");
        using var client = _authed.CreateClientFor("petitioner-a");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"))).StatusCode);
    }

    [Fact]
    public async Task ParallelRequests_WithOneToken_YieldExactlyOneSuccess()
    {
        const int contenders = 24;
        var id = SeedToken("petitioner-a");
        using var client = _authed.CreateClientFor("petitioner-a");
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return (await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"))).StatusCode;
        })).ToArray();

        gate.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(contenders - 1, statuses.Count(s => s == HttpStatusCode.Conflict));
        Assert.True(IsConsumed(id));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var id = SeedToken("petitioner-a", ttlSeconds: -5);
        using var client = _authed.CreateClientFor("petitioner-a");

        var response = await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(IsConsumed(id));
    }

    // ---- AMD-0002: topology through the pipeline ----------------------------------------------

    [Theory]
    [InlineData("/docs/ratified/../x")]
    [InlineData("/docs/ratified/../../etc/passwd")]
    [InlineData("/docs/ratified-evil/x")]
    [InlineData("/docs/ratified/%2e%2e/x")]
    [InlineData("/docs/rejected/a.md")]
    public async Task PathsOutsideTheEnvelope_AreForbidden_AndDoNotBurnTheToken(string path)
    {
        var id = SeedToken("petitioner-a");
        using var client = _authed.CreateClientFor("petitioner-a");

        var response = await client.PostAsJsonAsync("/Mutation/execute", Execute(id, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(IsConsumed(id));

        var legit = await client.PostAsJsonAsync("/Mutation/execute", Execute(id, "/docs/ratified/a.md"));
        Assert.Equal(HttpStatusCode.OK, legit.StatusCode);
    }
}

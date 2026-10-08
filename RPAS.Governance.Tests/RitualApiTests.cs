using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0007 through the real HTTP pipeline: scoped tokens, human-only scope, versioned definitions.</summary>
public sealed class RitualApiTests : IDisposable
{
    private const string Human = "11111111-1111-1111-1111-111111111111";
    private const string Stranger = "22222222-2222-2222-2222-222222222222";

    private readonly ApiFactory _factory = new(useTestAuth: true, new Dictionary<string, string?>
    {
        // sidpa may broker every scope; sidpa-heal only heals; nobody else has a grant (fail closed).
        ["Governance:Petitioners:sidpa:Scopes"] = "declare-intent,implement,heal,edit-contract",
        ["Governance:Petitioners:sidpa-heal:Scopes"] = "heal",
        ["Governance:Governors:0"] = Human,
    });

    public RitualApiTests() => _factory.EnsureDatabase();

    public void Dispose() => _factory.Dispose();

    private List<GovernanceLedgerEntry> Ledger()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        return db.GovernanceLedgerEntries.AsNoTracking().ToList().OrderBy(e => e.Sequence ?? long.MaxValue).ToList();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Post(HttpClient client, string route, object body)
    {
        var response = await client.PostAsJsonAsync(route, body);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> Issue(HttpClient client, string ritual, string entity = "mod-1") =>
        Post(client, "/Tokens/issue", new { RitualType = ritual, EntityId = entity });

    private static Task<(HttpStatusCode Status, JsonElement Body)> Execute(HttpClient client, string tokenId, string path) =>
        Post(client, "/Mutation/execute", new { TokenId = tokenId, TargetPath = path });

    // ---- authentication and fail-closed defaults ------------------------------------------------------------------

    [Theory]
    [InlineData("POST", "/Tokens/issue")]
    [InlineData("GET", "/Rituals/definitions")]
    [InlineData("POST", "/Rituals/definitions")]
    public async Task EndpointsRequireAuthentication(string method, string route)
    {
        using var client = _factory.CreateClientFor(petitioner: null);
        var response = method == "POST" ? await client.PostAsJsonAsync(route, new { }) : await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task APetitionerWithNoScopeGrant_GetsNoToken_AndNothingIsWritten()
    {
        using var client = _factory.CreateClientFor("nobody");
        var (status, _) = await Issue(client, "Heal");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.DoesNotContain(Ledger(), e => e.RitualType == "TokenIssued");
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().AuthorityTokens);
    }

    [Fact]
    public async Task AGrantForOneScope_DoesNotGiveAnother()
    {
        using var client = _factory.CreateClientFor("sidpa-heal");
        Assert.Equal(HttpStatusCode.Created, (await Issue(client, "Heal")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(client, "Implement")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(client, "DeclareIntent")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(client, "EditContract")).Status);
    }

    [Theory]
    [InlineData("Create")]            // an existing BusinessCase action carries no scope
    [InlineData("EvidenceRecorded")]  // an evidence ritual issues no token
    [InlineData("NoSuchRitual")]
    [InlineData("")]
    public async Task OnlyScopedRituals_IssueTokens(string ritual)
    {
        using var client = _factory.CreateClientFor("sidpa");
        Assert.Equal(HttpStatusCode.BadRequest, (await Issue(client, ritual)).Status);
    }

    [Theory]
    [InlineData("Jane Doe")]
    [InlineData("a@b.c")]
    [InlineData("")]
    public async Task EntityId_MustBeAnIdentifier(string entity)
    {
        using var client = _factory.CreateClientFor("sidpa");
        Assert.Equal(HttpStatusCode.BadRequest, (await Issue(client, "Heal", entity)).Status);
    }

    // ---- issuance, binding and the ledger -------------------------------------------------------------------------

    [Fact]
    public async Task ATokenIsIssued_WithItsScopeAndEnvelope_AndTheIssuanceIsOnTheLedger()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var (status, body) = await Issue(client, "Implement", "mod-7");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("implement", body.GetProperty("scope").GetString());
        Assert.Contains("/governed/implementation/*", body.GetProperty("allowedPaths").EnumerateArray().Select(p => p.GetString()));

        var entry = Assert.Single(Ledger(), e => e.RitualType == "TokenIssued");
        Assert.Equal("sidpa", entry.PetitionerId);
        Assert.Equal(GovernanceLedgerEntry.ContentModeHashOnly, entry.ContentMode);
        using var proof = JsonDocument.Parse(entry.ProofJson!);
        Assert.Equal(body.GetProperty("id").GetString(), proof.RootElement.GetProperty("tokenId").GetString());
        Assert.Equal("implement", proof.RootElement.GetProperty("scope").GetString());
        Assert.Equal("mod-7", proof.RootElement.GetProperty("entityId").GetString());
        Assert.Equal(1, proof.RootElement.GetProperty("definitionVersion").GetInt32());
        Assert.Equal(64, proof.RootElement.GetProperty("definitionHash").GetString()!.Length);
    }

    [Fact]
    public async Task ATokenIsBoundToThePetitionerItWasIssuedTo()
    {
        using var owner = _factory.CreateClientFor("sidpa");
        using var thief = _factory.CreateClientFor("sidpa-heal");
        var (_, token) = await Issue(owner, "Heal");

        var stolen = await Execute(thief, token.GetProperty("id").GetString()!, "/governed/implementation/m/x");
        Assert.Equal(HttpStatusCode.Forbidden, stolen.Status);

        var own = await Execute(owner, token.GetProperty("id").GetString()!, "/governed/implementation/m/x");
        Assert.Equal(HttpStatusCode.OK, own.Status);
        Assert.Equal("heal", own.Body.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task ATokenIsSingleUse()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var (_, token) = await Issue(client, "Implement");
        var id = token.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await Execute(client, id, "/governed/implementation/m/x")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Execute(client, id, "/governed/implementation/m/x")).Status);
    }

    // ---- exit criterion 5: a heal token cannot edit contracts or intent ----------------------------------------------

    [Theory]
    [InlineData("/governed/contracts/suite-1")]
    [InlineData("/governed/contracts/suite-1/case.json")]
    [InlineData("/governed/intent/mod-1")]
    [InlineData("/governed/implementation/../contracts/suite-1")]   // traversal into the contract area
    [InlineData("/governed/contracts-archive/../contracts/x")]
    public async Task AHealToken_IsBlockedFromContractsAndIntent_AndIsNotBurnedByTheAttempt(string forbiddenPath)
    {
        using var client = _factory.CreateClientFor("sidpa");
        var (_, token) = await Issue(client, "Heal");
        var id = token.GetProperty("id").GetString()!;

        var blocked = await Execute(client, id, forbiddenPath);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.Status);
        Assert.Equal("Topology Violation (G6)", blocked.Body.GetProperty("error").GetString());

        // The token is still good for its own territory: the violation did not consume it.
        Assert.Equal(HttpStatusCode.OK, (await Execute(client, id, "/governed/implementation/mod-1/heal.ts")).Status);
    }

    [Fact]
    public async Task AnImplementToken_IsAlsoBlockedFromContractsAndIntent()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var (_, token) = await Issue(client, "Implement");
        var id = token.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden, (await Execute(client, id, "/governed/contracts/suite-1")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Execute(client, id, "/governed/intent/mod-1")).Status);
    }

    [Fact]
    public async Task EachScope_ReachesOnlyItsOwnArea()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Human);
        var intent = (await Issue(client, "DeclareIntent")).Body.GetProperty("id").GetString()!;
        var contract = (await Issue(client, "EditContract")).Body.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden, (await Execute(client, intent, "/governed/contracts/x")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Execute(client, contract, "/governed/intent/x")).Status);
        Assert.Equal(HttpStatusCode.OK, (await Execute(client, intent, "/governed/intent/x")).Status);
        Assert.Equal(HttpStatusCode.OK, (await Execute(client, contract, "/governed/contracts/x")).Status);
    }

    // ---- edit-contract is for a named human only -------------------------------------------------------------------

    [Fact]
    public async Task EditContract_IsRefusedToAnAutomatedPetitioner_EvenWithTheScopeGranted()
    {
        using var client = _factory.CreateClientFor("sidpa"); // client-credentials style: no user
        var (status, body) = await Issue(client, "EditContract");
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("named human", body.GetProperty("error").GetString());
        Assert.DoesNotContain(Ledger(), e => e.RitualType == "TokenIssued");
    }

    [Fact]
    public async Task EditContract_IsRefusedToAnAppOnlyTokenThatCarriesAnOid()
    {
        // A service principal has an object id too, but it is an app token: "roles" and idtyp=app, never "scp".
        using var client = _factory.CreateClientFor("sidpa", appOid: Human);
        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(client, "EditContract")).Status);
    }

    [Fact]
    public async Task EditContract_IsIssuedToANamedHuman_AndTheHumanIsOnTheTokenAndTheLedger()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Stranger); // any signed-in human of a granted petitioner
        var (status, body) = await Issue(client, "EditContract", "suite-1");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("edit-contract", body.GetProperty("scope").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        var token = await db.AuthorityTokens.AsNoTracking().SingleAsync();
        Assert.Equal(Stranger, token.HumanId);
        Assert.Equal("sidpa", token.PetitionerId);

        var entry = Assert.Single(Ledger(), e => e.RitualType == "TokenIssued");
        using var proof = JsonDocument.Parse(entry.ProofJson!);
        Assert.Equal(Stranger, proof.RootElement.GetProperty("humanId").GetString());
    }

    [Fact]
    public async Task APetitionerWithoutTheEditContractGrant_CannotBrokerItEvenForAHuman()
    {
        using var client = _factory.CreateClientFor("sidpa-heal", human: Human);
        Assert.Equal(HttpStatusCode.Forbidden, (await Issue(client, "EditContract")).Status);
    }

    [Fact]
    public async Task ANonHumanScope_DoesNotRecordAHuman()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Human);
        await Issue(client, "Heal");
        using var scope = _factory.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().AuthorityTokens.AsNoTracking().SingleAsync()).HumanId);
    }

    // ---- reading definitions --------------------------------------------------------------------------------------

    [Fact]
    public async Task ListShowsTheSeededDefinitionsInForce()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var list = await client.GetFromJsonAsync<JsonElement>("/Rituals/definitions");
        var names = list.EnumerateArray().Select(d => d.GetProperty("ritualType").GetString()).ToHashSet();

        foreach (var expected in new[]
                 { "DeclareIntent", "Implement", "Heal", "EditContract", "IntentDeclared", "ContractResult", "HealAttempt",
                   "EvidenceRecorded", "ScoreAttested", "HumanAttestation", "Create", "MarkApproved", "MarkRejected", "OverrideRitual" })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public async Task SeedingIsOneLedgerEvent_AndHappensOnce()
    {
        using var client = _factory.CreateClientFor("sidpa");
        await client.GetAsync("/Rituals/definitions");
        await client.GetAsync("/Rituals/definitions");
        await Issue(client, "Heal");

        var seeds = Ledger().Where(e => e.RitualType == RitualDefinitionStore.ChangeRitualType).ToList();
        Assert.Single(seeds);
        using var proof = JsonDocument.Parse(seeds[0].ProofJson!);
        Assert.Equal("seed", proof.RootElement.GetProperty("action").GetString());
        Assert.Equal(RitualSeed.Definitions.Count, proof.RootElement.GetProperty("definitionCount").GetInt32());

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(RitualSeed.Definitions.Count, await scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().RitualDefinitions.CountAsync());
    }

    [Fact]
    public async Task ConcurrentFirstRequests_SeedExactlyOnce()
    {
        var clients = Enumerable.Range(0, 6).Select(_ => _factory.CreateClientFor("sidpa")).ToList();
        var responses = await Task.WhenAll(clients.Select(c => c.GetAsync("/Rituals/definitions")));
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));

        Assert.Single(Ledger(), e => e.RitualType == RitualDefinitionStore.ChangeRitualType);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(RitualSeed.Definitions.Count, await scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().RitualDefinitions.CountAsync());
        foreach (var c in clients) c.Dispose();
    }

    // ---- publishing definitions: governors only, ledgered ------------------------------------------------------------

    private static object Definition(string ritual = "Heal", string? petitioner = null, string? scope = "heal",
        string[]? paths = null, bool retired = false, bool evidence = false, string[]? keys = null) => new
    {
        PetitionerId = petitioner,
        RitualType = ritual,
        IsRetired = retired,
        AcceptsEvidence = evidence,
        MetadataKeys = keys,
        Scope = scope,
        AllowedPaths = paths ?? ["/governed/implementation/*", "/governed/healing/*"]
    };

    [Fact]
    public async Task AutomatedPetitioners_CannotPublish()
    {
        using var client = _factory.CreateClientFor("sidpa");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/Rituals/definitions", Definition())).Status);
    }

    [Fact]
    public async Task AnAppOnlyToken_EvenWithAGovernorOid_CannotPublish()
    {
        using var client = _factory.CreateClientFor("sidpa", appOid: Human);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/Rituals/definitions", Definition())).Status);
    }

    [Fact]
    public async Task AHumanWhoIsNotAGovernor_CannotPublish()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Stranger);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/Rituals/definitions", Definition())).Status);
        Assert.DoesNotContain(Ledger(), e => e.ProofJson is not null && e.ProofJson.Contains("\"publish\""));
    }

    [Fact]
    public async Task WithNoGovernorsConfigured_NobodyCanPublish()
    {
        using var factory = new ApiFactory(useTestAuth: true);
        factory.EnsureDatabase();
        using var client = factory.CreateClientFor("sidpa", human: Human);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/Rituals/definitions", Definition())).Status);
    }

    [Fact]
    public async Task AGovernorPublishesANewVersion_AndTheChangeIsALedgerEventNamingThem()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Human);
        var (status, body) = await Post(client, "/Rituals/definitions", Definition());

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, body.GetProperty("version").GetInt32()); // the seed is version 1
        var hash = body.GetProperty("definitionHash").GetString();

        var change = Ledger().Last(e => e.RitualType == RitualDefinitionStore.ChangeRitualType);
        using var proof = JsonDocument.Parse(change.ProofJson!);
        Assert.Equal("publish", proof.RootElement.GetProperty("action").GetString());
        Assert.Equal("Heal", proof.RootElement.GetProperty("ritualType").GetString());
        Assert.Equal(2, proof.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(hash, proof.RootElement.GetProperty("definitionHash").GetString());
        Assert.Equal(Human, proof.RootElement.GetProperty("changedBy").GetString());
        Assert.Equal("sidpa", proof.RootElement.GetProperty("viaPetitioner").GetString());
    }

    [Fact]
    public async Task ANewVersion_TakesEffectForNewTokens_AndOldVersionsStayInTheStore()
    {
        using var human = _factory.CreateClientFor("sidpa", human: Human);
        using var app = _factory.CreateClientFor("sidpa");
        await Post(human, "/Rituals/definitions", Definition(paths: ["/governed/implementation/*", "/governed/healing/*"]));

        var (_, token) = await Issue(app, "Heal");
        Assert.Equal(HttpStatusCode.OK, (await Execute(app, token.GetProperty("id").GetString()!, "/governed/healing/x")).Status);

        using var scope = _factory.Services.CreateScope();
        var versions = await scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().RitualDefinitions.AsNoTracking()
            .Where(d => d.RitualType == "Heal").Select(d => d.Version).OrderBy(v => v).ToListAsync();
        Assert.Equal([1, 2], versions);

        var entry = Assert.Single(Ledger(), e => e.RitualType == "TokenIssued");
        using var proof = JsonDocument.Parse(entry.ProofJson!);
        Assert.Equal(2, proof.RootElement.GetProperty("definitionVersion").GetInt32());
    }

    [Fact]
    public async Task APublishedDefinition_CannotGiveHealAccessToContractsOrIntent()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Human);

        var contracts = await Post(client, "/Rituals/definitions", Definition(paths: ["/governed/implementation/*", "/governed/contracts/*"]));
        var intent = await Post(client, "/Rituals/definitions", Definition(paths: ["/governed/intent/*"]));

        Assert.Equal(HttpStatusCode.BadRequest, contracts.Status);
        Assert.Equal(HttpStatusCode.BadRequest, intent.Status);
        Assert.DoesNotContain(Ledger(), e => e.ProofJson is not null && e.ProofJson.Contains("\"publish\""));
    }

    [Fact]
    public async Task AnInvalidDefinition_WritesNothing()
    {
        using var client = _factory.CreateClientFor("sidpa", human: Human);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/Rituals/definitions", Definition(ritual: "Bad Name"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/Rituals/definitions", Definition(scope: "root"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/Rituals/definitions", Definition(paths: ["/a/../b/*"]))).Status);

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().RitualDefinitions.CountAsync(d => d.Version > 1));
    }

    [Fact]
    public async Task APetitionerSpecificDefinition_OverridesTheDefault_OnlyForThatPetitioner()
    {
        using var human = _factory.CreateClientFor("sidpa", human: Human);
        // sidpa-heal gets a narrower heal envelope than the default.
        await Post(human, "/Rituals/definitions", Definition(petitioner: "sidpa-heal", paths: ["/governed/implementation/healing-only/*"]));

        using var narrow = _factory.CreateClientFor("sidpa-heal");
        using var normal = _factory.CreateClientFor("sidpa");

        var narrowToken = (await Issue(narrow, "Heal")).Body.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Forbidden, (await Execute(narrow, narrowToken, "/governed/implementation/other/x")).Status);
        Assert.Equal(HttpStatusCode.OK, (await Execute(narrow, narrowToken, "/governed/implementation/healing-only/x")).Status);

        var normalToken = (await Issue(normal, "Heal")).Body.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Execute(normal, normalToken, "/governed/implementation/other/x")).Status);
    }

    [Fact]
    public async Task ARetiredRitual_IsUnavailable_WithNoFallBackToTheDefault()
    {
        using var human = _factory.CreateClientFor("sidpa", human: Human);
        await Post(human, "/Rituals/definitions", Definition(petitioner: "sidpa-heal", retired: true, scope: null, paths: []));

        using var retired = _factory.CreateClientFor("sidpa-heal");
        Assert.Equal(HttpStatusCode.BadRequest, (await Issue(retired, "Heal")).Status);

        var list = await retired.GetFromJsonAsync<JsonElement>("/Rituals/definitions");
        Assert.DoesNotContain(list.EnumerateArray(), d => d.GetProperty("ritualType").GetString() == "Heal");

        using var other = _factory.CreateClientFor("sidpa");
        Assert.Equal(HttpStatusCode.Created, (await Issue(other, "Heal")).Status); // others still have the default

        var change = Ledger().Last(e => e.RitualType == RitualDefinitionStore.ChangeRitualType);
        using var proof = JsonDocument.Parse(change.ProofJson!);
        Assert.Equal("retire", proof.RootElement.GetProperty("action").GetString());
    }

    // ---- hash-only evidence now follows the definitions ---------------------------------------------------------------

    private static object Evidence(string ritual, Dictionary<string, object?> metadata) => new
    {
        RitualType = ritual,
        EntityId = "doc-1",
        ContentHash = new string('a', 64),
        HashAlgorithm = "SHA-256",
        Metadata = metadata
    };

    [Fact]
    public async Task EvidenceMetadataKeys_AreTheDefinitionsKeys_AndAChangeTakesEffect()
    {
        using var app = _factory.CreateClientFor("sidpa");
        using var human = _factory.CreateClientFor("sidpa", human: Human);

        var before = await Post(app, "/Evidence/record", Evidence("HealAttempt", new() { ["moduleId"] = "m1", ["costCentre"] = "cc-1" }));
        Assert.Equal(HttpStatusCode.BadRequest, before.Status); // costCentre is not an allowed key

        await Post(human, "/Rituals/definitions", Definition("HealAttempt", scope: null, paths: [], evidence: true,
            keys: ["moduleId", "attempt", "outcome", "scope", "costCentre"]));

        var after = await Post(app, "/Evidence/record", Evidence("HealAttempt", new() { ["moduleId"] = "m1", ["costCentre"] = "cc-1" }));
        Assert.Equal(HttpStatusCode.Created, after.Status);
    }

    [Fact]
    public async Task ARetiredEvidenceRitual_IsNoLongerAccepted()
    {
        using var human = _factory.CreateClientFor("sidpa", human: Human);
        using var app = _factory.CreateClientFor("sidpa");
        Assert.Equal(HttpStatusCode.Created, (await Post(app, "/Evidence/record", Evidence("ScoreAttested", new() { ["documentId"] = "d1" }))).Status);

        await Post(human, "/Rituals/definitions", Definition("ScoreAttested", scope: null, paths: [], retired: true));

        var (status, body) = await Post(app, "/Evidence/record", Evidence("ScoreAttested", new() { ["documentId"] = "d1" }));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("not an allowed", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AScopedTokenRitual_IsNotAnEvidenceRitual()
    {
        using var app = _factory.CreateClientFor("sidpa");
        var (status, _) = await Post(app, "/Evidence/record", Evidence("Heal", new()));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ---- the whole chain, including every step above, still verifies --------------------------------------------------

    [Fact]
    public async Task TheChain_StillVerifies_AfterSeedingTokensAndDefinitionChanges()
    {
        using var human = _factory.CreateClientFor("sidpa", human: Human);
        using var app = _factory.CreateClientFor("sidpa");
        await Issue(app, "DeclareIntent");
        await Post(human, "/Rituals/definitions", Definition());
        await Issue(app, "Heal");
        await Post(app, "/Evidence/record", Evidence("IntentDeclared", new() { ["moduleId"] = "m1" }));

        var verify = await app.GetAsync("/Ledger/verify");
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var json = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("ok").GetBoolean());
    }
}

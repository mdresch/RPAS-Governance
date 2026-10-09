using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Tests;

/// <summary>
/// AMD-2026-10-01-0010: Cross-Repo Contract Tests asserting exact parity between the Courthouse API
/// and the @rpas/governance-client TypeScript SDK contract expectations.
/// </summary>
public sealed class CrossRepoContractTests : IDisposable
{
    private const string HumanOid = "11111111-1111-1111-1111-111111111111";

    private readonly ApiFactory _factory = new(useTestAuth: true, new Dictionary<string, string?>
    {
        ["Governance:Petitioners:sidpa:Scopes"] = "declare-intent,implement,heal,edit-contract",
        ["Governance:Governors:0"] = HumanOid,
    });

    public CrossRepoContractTests() => _factory.EnsureDatabase();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task DefinitionsContract_MatchesClientExpectations()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var response = await client.GetAsync("/Rituals/definitions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var definitions = doc.RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(definitions);

        foreach (var def in definitions)
        {
            // Verify all expected camelCase properties from RitualDefinition Summary contract
            Assert.True(def.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == JsonValueKind.Number);
            Assert.True(def.TryGetProperty("petitionerId", out var petProp) && petProp.ValueKind == JsonValueKind.String);
            Assert.True(def.TryGetProperty("ritualType", out var ritualProp) && ritualProp.ValueKind == JsonValueKind.String);
            Assert.True(def.TryGetProperty("acceptsEvidence", out var evProp) && (evProp.ValueKind == JsonValueKind.True || evProp.ValueKind == JsonValueKind.False));
            Assert.True(def.TryGetProperty("metadataKeys", out var keysProp) && keysProp.ValueKind == JsonValueKind.Array);
            Assert.True(def.TryGetProperty("allowedPaths", out var pathsProp) && pathsProp.ValueKind == JsonValueKind.Array);
            Assert.True(def.TryGetProperty("definitionHash", out var hashProp) && hashProp.GetString()?.Length == 64);
        }

        // Verify SIDPA scoped rituals are present
        var ritualTypes = definitions.Select(d => d.GetProperty("ritualType").GetString()).ToHashSet();
        Assert.Contains("DeclareIntent", ritualTypes);
        Assert.Contains("Implement", ritualTypes);
        Assert.Contains("Heal", ritualTypes);
        Assert.Contains("EditContract", ritualTypes);

        // Verify evidence rituals are present
        Assert.Contains("IntentDeclared", ritualTypes);
        Assert.Contains("ContractResult", ritualTypes);
        Assert.Contains("HealAttempt", ritualTypes);
        Assert.Contains("EvidenceRecorded", ritualTypes);
        Assert.Contains("ScoreAttested", ritualTypes);
        Assert.Contains("HumanAttestation", ritualTypes);
    }

    [Fact]
    public async Task TokenIssueContract_MatchesClientExpectations()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var response = await client.PostAsJsonAsync("/Tokens/issue", new
        {
            RitualType = "DeclareIntent",
            EntityId = "intent-contract-001"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out _));
        Assert.True(root.TryGetProperty("expiresAt", out var expiresProp) && DateTimeOffset.TryParse(expiresProp.GetString(), out _));
        Assert.Equal("DeclareIntent", root.GetProperty("ritualType").GetString());
        Assert.Equal("declare-intent", root.GetProperty("scope").GetString());

        var paths = root.GetProperty("allowedPaths").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("/governed/intent/*", paths);
    }

    [Fact]
    public async Task EvidenceRecordContract_MatchesClientExpectations()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var response = await client.PostAsJsonAsync("/Evidence/record", new
        {
            RitualType = "IntentDeclared",
            EntityId = "intent-contract-001",
            ContentHash = new string('a', 64),
            HashAlgorithm = "SHA-256",
            Metadata = new Dictionary<string, object>
            {
                ["moduleId"] = "module-core",
                ["intentVersion"] = "v1.0.0",
                ["standardId"] = "ISO-9001"
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out _));
        Assert.True(root.TryGetProperty("sequence", out var seqProp) && seqProp.GetInt64() > 0);
        Assert.True(root.TryGetProperty("entryHash", out var entryHashProp) && entryHashProp.GetString()?.Length == 64);
        Assert.True(root.TryGetProperty("prevHash", out var prevHashProp) && prevHashProp.GetString()?.Length == 64);
    }

    [Fact]
    public async Task MutationContract_MatchesClientExpectations_AndEnforcesReplayPrevention()
    {
        using var client = _factory.CreateClientFor("sidpa");

        // Step 1: Issue token
        var issueResp = await client.PostAsJsonAsync("/Tokens/issue", new
        {
            RitualType = "DeclareIntent",
            EntityId = "intent-mut-001"
        });
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        using var issueDoc = JsonDocument.Parse(await issueResp.Content.ReadAsStringAsync());
        var tokenId = issueDoc.RootElement.GetProperty("id").GetString()!;

        // Step 2: Execute effect
        var execResp = await client.PostAsJsonAsync("/Mutation/execute", new
        {
            TokenId = tokenId,
            TargetPath = "/governed/intent/spec.json"
        });

        Assert.Equal(HttpStatusCode.OK, execResp.StatusCode);
        using var execDoc = JsonDocument.Parse(await execResp.Content.ReadAsStringAsync());
        var execRoot = execDoc.RootElement;

        Assert.Equal("executed", execRoot.GetProperty("status").GetString());
        Assert.Equal("DeclareIntent", execRoot.GetProperty("ritualType").GetString());
        Assert.Equal("declare-intent", execRoot.GetProperty("scope").GetString());
        Assert.Equal("intent-mut-001", execRoot.GetProperty("entityId").GetString());
        Assert.Equal("/governed/intent/spec.json", execRoot.GetProperty("path").GetString());

        // Step 3: Replay detection (409 Conflict)
        var replayResp = await client.PostAsJsonAsync("/Mutation/execute", new
        {
            TokenId = tokenId,
            TargetPath = "/governed/intent/spec.json"
        });

        Assert.Equal(HttpStatusCode.Conflict, replayResp.StatusCode);
        using var replayDoc = JsonDocument.Parse(await replayResp.Content.ReadAsStringAsync());
        var replayError = replayDoc.RootElement.GetProperty("error").GetString();
        Assert.Contains("Replay detected", replayError);
    }

    [Fact]
    public async Task TopologyViolation_Returns403_AndLeavesTokenUnburned()
    {
        using var client = _factory.CreateClientFor("sidpa");

        var issueResp = await client.PostAsJsonAsync("/Tokens/issue", new
        {
            RitualType = "DeclareIntent",
            EntityId = "intent-top-001"
        });
        Assert.Equal(HttpStatusCode.Created, issueResp.StatusCode);
        using var issueDoc = JsonDocument.Parse(await issueResp.Content.ReadAsStringAsync());
        var tokenId = issueDoc.RootElement.GetProperty("id").GetString()!;

        // Illegal path breach (attempting to write outside declared /governed/intent envelope)
        var breachResp = await client.PostAsJsonAsync("/Mutation/execute", new
        {
            TokenId = tokenId,
            TargetPath = "/governed/intent/../contracts/breach.ts"
        });

        Assert.Equal(HttpStatusCode.Forbidden, breachResp.StatusCode);
        using var breachDoc = JsonDocument.Parse(await breachResp.Content.ReadAsStringAsync());
        Assert.Equal("Topology Violation (G6)", breachDoc.RootElement.GetProperty("error").GetString());
        Assert.Equal("/governed/intent/../contracts/breach.ts", breachDoc.RootElement.GetProperty("path").GetString());

        // Unburned token can now be legally consumed on an authorized path
        var legalResp = await client.PostAsJsonAsync("/Mutation/execute", new
        {
            TokenId = tokenId,
            TargetPath = "/governed/intent/legal.json"
        });
        Assert.Equal(HttpStatusCode.OK, legalResp.StatusCode);
    }

    [Fact]
    public async Task HumanOnlyScope_RejectsAutomatedPetitioner()
    {
        // Calling without human header
        using var client = _factory.CreateClientFor("sidpa");
        var response = await client.PostAsJsonAsync("/Tokens/issue", new
        {
            RitualType = "EditContract",
            EntityId = "contract-001"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = doc.RootElement.GetProperty("error").GetString();
        Assert.Contains("named human only", error);
    }

    [Fact]
    public async Task LedgerContract_MatchesClientExpectations()
    {
        using var client = _factory.CreateClientFor("sidpa");

        // GET /Ledger/head
        var headResp = await client.GetAsync("/Ledger/head");
        Assert.Equal(HttpStatusCode.OK, headResp.StatusCode);
        using var headDoc = JsonDocument.Parse(await headResp.Content.ReadAsStringAsync());
        Assert.True(headDoc.RootElement.TryGetProperty("sequence", out _));
        Assert.True(headDoc.RootElement.TryGetProperty("entryHash", out _));
        Assert.True(headDoc.RootElement.TryGetProperty("chainOk", out var chainOkProp) && chainOkProp.GetBoolean());

        // GET /Ledger/verify
        var verifyResp = await client.GetAsync("/Ledger/verify");
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        using var verifyDoc = JsonDocument.Parse(await verifyResp.Content.ReadAsStringAsync());
        Assert.True(verifyDoc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean());
        Assert.True(verifyDoc.RootElement.TryGetProperty("chain", out var chainProp));
        Assert.True(chainProp.GetProperty("ok").GetBoolean());
    }
}


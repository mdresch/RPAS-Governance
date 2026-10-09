using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>
/// AMD-2026-10-01-0011: Ledger Replay and Audit Verification Engine.
/// Fulfills CSR-42 Audit Brief §7 requirements for deterministic state reconstitution,
/// tamper detection, and multi-agent token replay protection.
/// </summary>
public sealed class LedgerReplayTests : IDisposable
{
    private readonly ApiFactory _factory = new(useTestAuth: true);

    public LedgerReplayTests()
    {
        _factory.EnsureDatabase();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private void WithDb(Action<GovernanceDbContext> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        action(db);
    }

    private async Task AppendChainEntriesAsync(params GovernanceLedgerEntry[] entries)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        db.GovernanceLedgerEntries.AddRange(entries);
        await LedgerChain.SaveChangesAsync(db);
    }

    private void RawSql(string sql)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        db.Database.ExecuteSqlRaw(sql);
    }

    private Guid SeedToken(string petitionerId, string ritual = "MarkApproved", int ttlSeconds = 120)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        var token = new AuthorityToken(ritual, "entity-replay-001", ttlSeconds, petitionerId);
        foreach (var p in RitualSeed.PathsFor(ritual))
        {
            token.AllowedPaths.Add(p);
        }
        db.AuthorityTokens.Add(token);
        db.SaveChanges();
        return token.Id;
    }

    [Fact]
    public async Task EmptyLedger_ReplaySucceedsWithZeroCounts()
    {
        using var client = _factory.CreateClientFor("petitioner-a");

        var response = await client.GetAsync("/Ledger/replay");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("eventsReplayed").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("headSequence").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("headHash").ValueKind);

        var state = root.GetProperty("state");
        Assert.Equal(0, state.GetProperty("activeDefinitions").GetInt32());
        Assert.Equal(0, state.GetProperty("issuedTokens").GetInt32());
        Assert.Equal(0, state.GetProperty("evidenceRecords").GetInt32());
        Assert.Equal(0, state.GetProperty("businessCases").GetInt32());

        Assert.Equal(0, root.GetProperty("problems").GetArrayLength());
    }

    [Fact]
    public async Task SeededLedger_ReplayDeterministicallyReconstructsAllStateCounts()
    {
        // Append entries across all ritual types
        await AppendChainEntriesAsync(
            GovernanceLedgerEntry.CreateHashOnly("RitualDefinitionChanged", "petitioner-a", "{\"definitionId\":\"def-1\"}"),
            GovernanceLedgerEntry.CreateHashOnly("TokenIssued", "petitioner-a", "{\"tokenId\":\"tok-1\"}"),
            GovernanceLedgerEntry.CreateHashOnly("TokenIssued", "petitioner-a", "{\"tokenId\":\"tok-2\"}"),
            GovernanceLedgerEntry.CreateHashOnly("IntentDeclared", "petitioner-a", "{\"intent\":\"doc-1\"}"),
            GovernanceLedgerEntry.CreateHashOnly("ContractResult", "petitioner-a", "{\"test\":\"passed\"}"),
            GovernanceLedgerEntry.CreateHashOnly("ScoreAttested", "petitioner-a", "{\"score\":0.99}"),
            GovernanceLedgerEntry.CreateHashOnly("HumanAttestation", "petitioner-a", "{\"attestor\":\"human-1\"}"),
            GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "petitioner-a", "{\"hash\":\"abc\"}"),
            GovernanceLedgerEntry.CreateHashOnly("Create", "petitioner-a", "{\"case\":\"case-1\"}"),
            GovernanceLedgerEntry.CreateHashOnly("MarkApproved", "petitioner-a", "{\"case\":\"case-1\"}")
        );

        // Direct persistence verification
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
            var directReport = await LedgerVerifier.ReplayAsync(db);
            Assert.True(directReport.Ok);
            Assert.Equal(11, directReport.EventsReplayed); // 1 Genesis + 10 entries
            Assert.Equal(11, directReport.HeadSequence);
            Assert.NotNull(directReport.HeadHash);
            Assert.Equal(1, directReport.State.ActiveDefinitions);
            Assert.Equal(2, directReport.State.IssuedTokens);
            Assert.Equal(5, directReport.State.EvidenceRecords); // IntentDeclared, ContractResult, ScoreAttested, HumanAttestation, EvidenceRecorded
            Assert.Equal(2, directReport.State.BusinessCases);   // Create, MarkApproved
            Assert.Empty(directReport.Problems);
        }

        // HTTP API verification
        using var client = _factory.CreateClientFor("petitioner-a");
        var response = await client.GetAsync("/Ledger/replay");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(11, root.GetProperty("eventsReplayed").GetInt64());
        Assert.Equal(11, root.GetProperty("headSequence").GetInt64());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("headHash").GetString()));

        var state = root.GetProperty("state");
        Assert.Equal(1, state.GetProperty("activeDefinitions").GetInt32());
        Assert.Equal(2, state.GetProperty("issuedTokens").GetInt32());
        Assert.Equal(5, state.GetProperty("evidenceRecords").GetInt32());
        Assert.Equal(2, state.GetProperty("businessCases").GetInt32());
        Assert.Equal(0, root.GetProperty("problems").GetArrayLength());
    }

    [Fact]
    public async Task TamperDetection_AlteredProofJson_Returns409WithExactSequence()
    {
        await AppendChainEntriesAsync(
            GovernanceLedgerEntry.CreateHashOnly("IntentDeclared", "petitioner-a", "{\"doc\":1}"),
            GovernanceLedgerEntry.CreateHashOnly("ContractResult", "petitioner-a", "{\"doc\":2}")
        );

        // Sequence 3 is ContractResult (Sequence 1 is Genesis, 2 is IntentDeclared, 3 is ContractResult)
        RawSql("UPDATE governance_ledger SET ProofJson = '{{\"doc\":\"tampered\"}}' WHERE Sequence = 3");

        using var client = _factory.CreateClientFor("petitioner-a");
        var response = await client.GetAsync("/Ledger/replay");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        var problems = root.GetProperty("problems");
        Assert.True(problems.GetArrayLength() > 0);

        var firstProblem = problems[0].GetString();
        Assert.Contains("Sequence 3", firstProblem);
        Assert.Contains("tampering detected", firstProblem);
    }

    [Fact]
    public async Task TamperDetection_BrokenPrevHash_Returns409WithPrevHashMismatch()
    {
        await AppendChainEntriesAsync(
            GovernanceLedgerEntry.CreateHashOnly("IntentDeclared", "petitioner-a", "{\"doc\":1}"),
            GovernanceLedgerEntry.CreateHashOnly("ContractResult", "petitioner-a", "{\"doc\":2}")
        );

        // Corrupt PrevHash of sequence 2
        var badHash = new string('0', 64);
        RawSql($"UPDATE governance_ledger SET PrevHash = '{badHash}' WHERE Sequence = 2");

        using var client = _factory.CreateClientFor("petitioner-a");
        var response = await client.GetAsync("/Ledger/replay");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        var problems = root.GetProperty("problems");
        Assert.True(problems.GetArrayLength() > 0);

        var problemText = problems[0].GetString();
        Assert.Contains("Sequence 2", problemText);
        Assert.Contains("PrevHash mismatch", problemText);
    }

    [Fact]
    public async Task TamperDetection_DeletedEntryGap_Returns409WithSequenceGap()
    {
        await AppendChainEntriesAsync(
            GovernanceLedgerEntry.CreateHashOnly("IntentDeclared", "petitioner-a", "{\"doc\":1}"),
            GovernanceLedgerEntry.CreateHashOnly("ContractResult", "petitioner-a", "{\"doc\":2}")
        );

        // Delete sequence 2 to create a sequence gap
        RawSql("DELETE FROM governance_ledger WHERE Sequence = 2");

        using var client = _factory.CreateClientFor("petitioner-a");
        var response = await client.GetAsync("/Ledger/replay");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        var problems = root.GetProperty("problems");
        Assert.True(problems.GetArrayLength() > 0);

        var problemText = string.Join(" | ", problems.EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("gap or mismatch", problemText);
    }

    [Fact]
    public async Task ConcurrencyStress_32Contenders_SingleAuthorityToken_ExactlyOneWins()
    {
        const int contenders = 32;
        var tokenId = SeedToken("petitioner-a", "MarkApproved");
        using var client = _factory.CreateClientFor("petitioner-a");

        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            var response = await client.PostAsJsonAsync("/Mutation/execute", new
            {
                TokenId = tokenId.ToString(),
                TargetPath = "/docs/ratified/a.md"
            });
            var content = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, content);
        })).ToArray();

        // Release the thundering herd simultaneously
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(contenders - 1, conflictCount);

        // Check that all 409 responses carry the explicit TokenReplayRule
        foreach (var (_, content) in results.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            using var doc = JsonDocument.Parse(content);
            Assert.Equal("TokenReplayRule", doc.RootElement.GetProperty("rule").GetString());
            Assert.Contains("Replay detected", doc.RootElement.GetProperty("error").GetString());
        }

        // Verify database state: token is consumed
        WithDb(db =>
        {
            var token = db.AuthorityTokens.Single(t => t.Id == tokenId);
            Assert.True(token.IsConsumed);
        });
    }
}


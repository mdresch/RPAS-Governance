using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0005/0006 through the real HTTP pipeline.</summary>
public sealed class EvidenceApiTests : IDisposable
{
    private readonly ApiFactory _factory = new(useTestAuth: true);

    public EvidenceApiTests() => _factory.EnsureDatabase();

    public void Dispose() => _factory.Dispose();

    private static object Evidence(string entity = "doc-42", string? documentId = "doc-42", object? extra = null) => new
    {
        RitualType = "EvidenceRecorded",
        EntityId = entity,
        ContentHash = new string('a', 64),
        HashAlgorithm = "SHA-256",
        Metadata = new Dictionary<string, object?>
        {
            ["documentId"] = documentId,
            ["documentVersion"] = 3,
            ["standardId"] = "ISO-27001"
        }
    };

    private List<GovernanceLedgerEntry> Ledger()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
        return db.GovernanceLedgerEntries.AsNoTracking().ToList().OrderBy(e => e.Sequence ?? long.MaxValue).ToList();
    }

    private void Raw(string sql)
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().Database.ExecuteSqlRaw(sql);
    }

    [Theory]
    [InlineData("POST", "/Evidence/record")]
    [InlineData("GET", "/Ledger/head")]
    [InlineData("GET", "/Ledger/verify")]
    public async Task EndpointsRequireAuthentication(string method, string route)
    {
        using var client = _factory.CreateClientFor(petitioner: null);
        var response = method == "POST"
            ? await client.PostAsJsonAsync(route, Evidence())
            : await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HashOnlyEvidence_IsChained_AndStoresOnlyProof()
    {
        using var client = _factory.CreateClientFor("sidpa"); // not listed => hash-only
        var response = await client.PostAsJsonAsync("/Evidence/record", Evidence());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("sequence").GetInt64()); // genesis is 1

        var entry = Assert.Single(Ledger(), e => e.Sequence == 2);
        Assert.Equal("EvidenceRecorded", entry.RitualType);
        Assert.Equal("sidpa", entry.PetitionerId);
        Assert.Equal(GovernanceLedgerEntry.ContentModeHashOnly, entry.ContentMode);
        Assert.Null(entry.IdeationJson);
        Assert.Null(entry.BusinessCaseJson);
        Assert.Equal(
            "{\"entityId\":\"doc-42\",\"contentHash\":\"" + new string('a', 64) + "\",\"hashAlgorithm\":\"SHA-256\",\"metadata\":{\"documentId\":\"doc-42\",\"documentVersion\":3,\"standardId\":\"ISO-27001\"}}",
            entry.ProofJson);
        Assert.Equal(body.GetProperty("entryHash").GetString(), entry.EntryHash);
    }

    [Fact]
    public async Task InvalidEvidence_IsRejected_WithoutEchoingValues()
    {
        using var client = _factory.CreateClientFor("sidpa");

        var response = await client.PostAsJsonAsync("/Evidence/record", Evidence(documentId: "Jane Doe jane@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Jane", text);
        Assert.DoesNotContain("example.com", text);
        Assert.DoesNotContain(Ledger(), e => e.RitualType == "EvidenceRecorded");
    }

    [Fact]
    public async Task NestedMetadata_IsRejected()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var payload = new
        {
            RitualType = "EvidenceRecorded", EntityId = "d", ContentHash = new string('a', 64), HashAlgorithm = "SHA-256",
            Metadata = new Dictionary<string, object?> { ["documentId"] = new { text = "full document body" } }
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/Evidence/record", payload)).StatusCode);
    }

    [Fact]
    public async Task HashOnlyPetitioner_CannotSendFullContentPetitions()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var petition = new
        {
            EntityType = "GovernanceLedgerEntry", EntityId = "e", Action = "Create",
            Payload = JsonDocument.Parse("{\"RitualType\":\"Create\",\"BusinessCaseJson\":\"confidential body\"}").RootElement
        };

        var response = await client.PostAsJsonAsync("/Validation/validate", petition);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(Ledger()); // nothing was written, not even a genesis entry
    }

    [Fact]
    public async Task SameRequest_ProducesTheSameProof_RegardlessOfMetadataOrder()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var a = JsonDocument.Parse("{\"ritualType\":\"EvidenceRecorded\",\"entityId\":\"d\",\"contentHash\":\"" + new string('a', 64) + "\",\"hashAlgorithm\":\"SHA-256\",\"metadata\":{\"standardId\":\"S\",\"documentId\":\"d\"}}").RootElement;
        var b = JsonDocument.Parse("{\"ritualType\":\"EvidenceRecorded\",\"entityId\":\"d\",\"contentHash\":\"" + new string('a', 64) + "\",\"hashAlgorithm\":\"SHA-256\",\"metadata\":{\"documentId\":\"d\",\"standardId\":\"S\"}}").RootElement;

        await client.PostAsJsonAsync("/Evidence/record", a);
        await client.PostAsJsonAsync("/Evidence/record", b);

        var proofs = Ledger().Where(e => e.RitualType == "EvidenceRecorded").Select(e => e.ProofJson).ToList();
        Assert.Equal(2, proofs.Count);
        Assert.Equal(proofs[0], proofs[1]);
    }

    [Fact]
    public async Task VerifyEndpoint_ReportsHealthyChain_ThenTampering()
    {
        using var client = _factory.CreateClientFor("sidpa");
        await client.PostAsJsonAsync("/Evidence/record", Evidence());
        await client.PostAsJsonAsync("/Evidence/record", Evidence(entity: "doc-43"));

        var healthy = await client.GetAsync("/Ledger/verify");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        var json = await healthy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal(3, json.GetProperty("chain").GetProperty("chainLength").GetInt64());

        Raw("UPDATE governance_ledger SET ProofJson = '{{\"entityId\":\"forged\"}}' WHERE Sequence = 2"); // SQLite has no trigger; Postgres tests cover the trigger

        var tampered = await client.GetAsync("/Ledger/verify");
        Assert.Equal(HttpStatusCode.Conflict, tampered.StatusCode);
        var bad = await tampered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(bad.GetProperty("ok").GetBoolean());
        Assert.Equal(2, bad.GetProperty("chain").GetProperty("firstBrokenSequence").GetInt64());
    }

    [Fact]
    public async Task HeadEndpoint_ReturnsTheChainHead()
    {
        using var client = _factory.CreateClientFor("sidpa");
        var created = await (await client.PostAsJsonAsync("/Evidence/record", Evidence())).Content.ReadFromJsonAsync<JsonElement>();

        var head = await client.GetFromJsonAsync<JsonElement>("/Ledger/head");

        Assert.Equal(2, head.GetProperty("sequence").GetInt64());
        Assert.Equal(created.GetProperty("entryHash").GetString(), head.GetProperty("entryHash").GetString());
    }

    // ---- full-content petitioners: sealed rows are never modified ---------------------------------------------------

    [Fact]
    public async Task OverrideOfAChainedEntry_IsRecordedAsANewEntry_AndTheOriginalIsUntouched()
    {
        using var client = _factory.CreateClientFor("petitioner-a"); // FullContent
        var originalId = Guid.NewGuid();

        var create = await client.PostAsJsonAsync("/Validation/validate", new
        {
            EntityType = "GovernanceLedgerEntry", EntityId = "x", Action = "Create",
            Payload = JsonDocument.Parse($"{{\"RitualType\":\"Create\",\"Id\":\"{originalId}\"}}").RootElement
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var before = Ledger().Single(e => e.Id == originalId);
        Assert.True(before.IsSealed);

        var override_ = await client.PostAsJsonAsync("/Validation/validate", new
        {
            EntityType = "GovernanceLedgerEntry", EntityId = originalId.ToString(), Action = "OverrideRitual",
            Payload = JsonDocument.Parse("{\"justification\":\"Governor decision\"}").RootElement
        });
        Assert.Equal(HttpStatusCode.OK, override_.StatusCode);

        var after = Ledger().Single(e => e.Id == originalId);
        Assert.Equal(before.EntryHash, after.EntryHash);
        Assert.False(after.IsOverridden);
        Assert.Equal("Completed", after.Status);

        var reference = Ledger().Single(e => e.RefersToEntryId == originalId);
        Assert.Equal("OverrideRitual", reference.RitualType);
        Assert.Contains("Governor decision", reference.BusinessCaseJson);
        Assert.Equal("petitioner-a", reference.PetitionerId);

        using var scope = _factory.Services.CreateScope();
        Assert.True((await LedgerVerifier.VerifyAsync(scope.ServiceProvider.GetRequiredService<GovernanceDbContext>())).Ok);
    }

    [Fact]
    public async Task PetitionerSuppliedTimestampAndChainFields_AreIgnored()
    {
        using var client = _factory.CreateClientFor("petitioner-a");
        var id = Guid.NewGuid();

        await client.PostAsJsonAsync("/Validation/validate", new
        {
            EntityType = "GovernanceLedgerEntry", EntityId = "x", Action = "Create",
            Payload = JsonDocument.Parse($"{{\"RitualType\":\"Create\",\"Id\":\"{id}\",\"InitiatedAt\":\"1999-01-01T00:00:00Z\",\"Sequence\":99,\"EntryHash\":\"forged\",\"PrevHash\":\"forged\"}}").RootElement
        });

        var entry = Ledger().Single(e => e.Id == id);
        Assert.NotEqual(99, entry.Sequence);
        Assert.NotEqual("forged", entry.EntryHash);
        Assert.True(entry.InitiatedAt.Year >= 2026);
    }

    [Fact]
    public async Task AnchoringWiredThroughConfiguration_VerifiesAnchors_AndFlagsTampering()
    {
        var anchorDir = Path.Combine(Path.GetTempPath(), "rpas-anchors-" + Guid.NewGuid().ToString("N"));
        using var factory = new ApiFactory(useTestAuth: true, new Dictionary<string, string?>
        {
            ["Governance:Anchoring:Provider"] = "File",
            ["Governance:Anchoring:Path"] = anchorDir,
            ["Governance:Anchoring:StartupDelaySeconds"] = "3600" // the test drives anchoring explicitly
        });
        factory.EnsureDatabase();

        try
        {
            using var client = factory.CreateClientFor("sidpa");
            await client.PostAsJsonAsync("/Evidence/record", Evidence());

            var anchor = await factory.Services.GetRequiredService<RPAS.Governance.Api.Anchoring.LedgerAnchorService>().AnchorOnceAsync();
            Assert.NotNull(anchor);

            var ok = await client.GetAsync("/Ledger/verify");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, json.GetProperty("anchors").GetProperty("anchorsChecked").GetInt32());
            Assert.True(json.GetProperty("anchors").GetProperty("ok").GetBoolean());

            using (var scope = factory.Services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<GovernanceDbContext>().Database
                    .ExecuteSqlRaw("UPDATE governance_ledger SET ProofJson = '{{}}' WHERE Sequence = 2");
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/Ledger/verify")).StatusCode);
        }
        finally
        {
            if (Directory.Exists(anchorDir))
            {
                foreach (var f in Directory.EnumerateFiles(anchorDir)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(anchorDir, true);
            }
        }
    }
}

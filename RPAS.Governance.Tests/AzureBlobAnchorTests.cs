using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RPAS.Governance.Api.Anchoring;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>Runs only when RPAS_TEST_AZURITE holds a storage connection string (the Azurite emulator, "UseDevelopmentStorage=true").</summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public const string EnvVar = "RPAS_TEST_AZURITE";

    public AzuriteFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"Set {EnvVar} (for example UseDevelopmentStorage=true with Azurite running) to run the Azure Blob sink tests.";
        }
    }
}

/// <summary>
/// AMD-2026-10-01-0005: the Azure Blob anchor sink, exercised against a real blob service (Azurite). This covers the sink's
/// own behaviour: create-only writes, idempotence, conflicting anchors kept and detected, and anchors catching a rewritten
/// history. What an emulator cannot prove is the container's time-based immutability policy; that is an operator setting in Azure.
/// </summary>
public sealed class AzureBlobAnchorTests : IDisposable
{
    private readonly string? _connection = Environment.GetEnvironmentVariable(AzuriteFactAttribute.EnvVar);
    private readonly string _containerName = "rpas-anchors-" + Guid.NewGuid().ToString("N")[..16];
    private readonly string _path = TestDatabase.NewPath();
    private readonly string _forgedPath = TestDatabase.NewPath();
    private readonly BlobContainerClient? _container;

    public AzureBlobAnchorTests()
    {
        if (string.IsNullOrWhiteSpace(_connection))
        {
            return; // tests are skipped
        }

        _container = new BlobContainerClient(_connection, _containerName);
        _container.CreateIfNotExists();
        TestDatabase.Create(_path);
        TestDatabase.Create(_forgedPath);
    }

    public void Dispose()
    {
        _container?.DeleteIfExists();
        TestDatabase.Delete(_path);
        TestDatabase.Delete(_forgedPath);
    }

    private AzureBlobLedgerAnchorSink Sink() => new(_container!);

    private List<BlobItem> Blobs() => _container!.GetBlobs(BlobTraits.None, BlobStates.None, "anchors/", CancellationToken.None).ToList();

    private static async Task AppendAsync(string path, string tag)
    {
        using var db = TestDatabase.NewContext(path);
        db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "p", $"{{\"entityId\":\"{tag}\"}}"));
        await LedgerChain.SaveChangesAsync(db);
    }

    private static LedgerAnchorService ServiceFor(string dbPath, ILedgerAnchorSink sink)
    {
        var provider = new ServiceCollection()
            .AddDbContext<GovernanceDbContext>(o => o.UseSqlite(TestDatabase.ConnectionString(dbPath)))
            .BuildServiceProvider();
        return new LedgerAnchorService(
            provider.GetRequiredService<IServiceScopeFactory>(), sink, NullLogger<LedgerAnchorService>.Instance,
            new ConfigurationBuilder().Build());
    }

    [AzuriteFact]
    public async Task AnAnchor_IsWrittenAsACreateOnlyBlob_AndReadBack()
    {
        var anchor = new LedgerAnchor(7, new string('a', 64), DateTimeOffset.UtcNow, LedgerHasher.Version, "azure-blob");
        await Sink().WriteAsync(anchor);

        var read = Assert.Single(await Sink().ReadAllAsync());
        Assert.Equal(7, read.Sequence);
        Assert.Equal(anchor.EntryHash, read.EntryHash);

        var blob = Assert.Single(Blobs());
        Assert.Equal("application/json", blob.Properties.ContentType);
    }

    [AzuriteFact]
    public async Task WritingTheSameAnchorAgain_IsIdempotent_AndChangesNothing()
    {
        var anchor = new LedgerAnchor(7, new string('a', 64), DateTimeOffset.UtcNow, LedgerHasher.Version, "azure-blob");
        await Sink().WriteAsync(anchor);
        var before = Assert.Single(Blobs()).Properties.ETag;

        await Sink().WriteAsync(anchor);

        var after = Assert.Single(Blobs()).Properties.ETag;
        Assert.Equal(before, after); // not rewritten
    }

    [AzuriteFact]
    public async Task AnExistingAnchorBlob_CannotBeOverwritten_ByACreateOnlyUpload()
    {
        var anchor = new LedgerAnchor(7, new string('a', 64), DateTimeOffset.UtcNow, LedgerHasher.Version, "azure-blob");
        await Sink().WriteAsync(anchor);
        var name = Assert.Single(Blobs()).Name;

        // The same conditional header the sink sends: a second create of that name is refused by the service.
        var ex = await Assert.ThrowsAsync<RequestFailedException>(() => _container!.GetBlobClient(name).UploadAsync(
            BinaryData.FromString("{}"),
            new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }));
        Assert.True(ex.Status is 409 or 412, $"unexpected status {ex.Status}");

        var kept = Assert.Single(await Sink().ReadAllAsync());
        Assert.Equal(anchor.EntryHash, kept.EntryHash);
    }

    [AzuriteFact]
    public async Task TwoAnchorsForOneSequence_AreBothKept_AndAreTheMarkOfTampering()
    {
        var anchor = new LedgerAnchor(5, new string('c', 64), DateTimeOffset.UtcNow, LedgerHasher.Version, "azure-blob");
        await Sink().WriteAsync(anchor);
        await Sink().WriteAsync(anchor with { EntryHash = new string('d', 64) });

        var all = await Sink().ReadAllAsync();
        Assert.Equal(2, all.Count);

        await using var db = TestDatabase.NewContext(_path);
        Assert.False((await LedgerVerifier.VerifyAgainstAnchorsAsync(db, all)).Ok);
    }

    [AzuriteFact]
    public async Task AnchoringEndToEnd_WritesBlobs_RecordsThemOnTheLedger_AndVerifies()
    {
        var sink = Sink();
        var service = ServiceFor(_path, sink);
        await AppendAsync(_path, "1");

        var first = await service.AnchorOnceAsync();
        Assert.NotNull(first);
        Assert.Null(await service.AnchorOnceAsync()); // nothing new

        await AppendAsync(_path, "2");
        Assert.NotNull(await service.AnchorOnceAsync());
        Assert.Equal(2, (await sink.ReadAllAsync()).Count);

        using var verify = TestDatabase.NewContext(_path);
        Assert.True((await LedgerVerifier.VerifyAsync(verify)).Ok);
        Assert.True((await LedgerVerifier.VerifyAgainstAnchorsAsync(verify, await sink.ReadAllAsync())).Ok);
    }

    [AzuriteFact]
    public async Task ARewrittenButSelfConsistentHistory_IsCaughtByTheBlobAnchors()
    {
        var sink = Sink();
        await AppendAsync(_path, "honest-1");
        await AppendAsync(_path, "honest-2");
        await ServiceFor(_path, sink).AnchorOnceAsync();

        await AppendAsync(_forgedPath, "FORGED-1");
        await AppendAsync(_forgedPath, "honest-2");
        await AppendAsync(_forgedPath, "padding");

        using var forged = TestDatabase.NewContext(_forgedPath);
        Assert.True((await LedgerVerifier.VerifyAsync(forged)).Ok); // the chain alone cannot see it

        var anchorCheck = await LedgerVerifier.VerifyAgainstAnchorsAsync(forged, await sink.ReadAllAsync());
        Assert.False(anchorCheck.Ok);
        Assert.Contains("history was rewritten", Assert.Single(anchorCheck.Problems));

        Assert.Null(await ServiceFor(_forgedPath, sink).AnchorOnceAsync()); // refuses to bless the forgery
        Assert.Single(await sink.ReadAllAsync());
    }

    [AzuriteFact]
    public async Task ARealBlobAnchor_IsRecordedAsHashOnlyEvidenceOnTheLedger()
    {
        var service = ServiceFor(_path, Sink());
        await AppendAsync(_path, "1");
        var anchor = await service.AnchorOnceAsync();

        using var db = TestDatabase.NewContext(_path);
        var entry = Assert.Single(db.GovernanceLedgerEntries, e => e.RitualType == LedgerAnchorService.AnchorRitualType);
        Assert.Equal(GovernanceLedgerEntry.ContentModeHashOnly, entry.ContentMode);
        Assert.Contains(anchor!.EntryHash, entry.ProofJson);
    }
}

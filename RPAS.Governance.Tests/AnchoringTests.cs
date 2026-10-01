using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RPAS.Governance.Api.Anchoring;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0005: external anchoring detects even a fully recomputed (rewritten) history.</summary>
public sealed class AnchoringTests : IDisposable
{
    private readonly string _path = TestDatabase.NewPath();
    private readonly string _forgedPath = TestDatabase.NewPath();
    private readonly string _anchorDir = Path.Combine(Path.GetTempPath(), "rpas-anchors-" + Guid.NewGuid().ToString("N"));

    public AnchoringTests()
    {
        TestDatabase.Create(_path);
        TestDatabase.Create(_forgedPath);
    }

    public void Dispose()
    {
        TestDatabase.Delete(_path);
        TestDatabase.Delete(_forgedPath);
        if (Directory.Exists(_anchorDir))
        {
            foreach (var f in Directory.EnumerateFiles(_anchorDir))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }
            Directory.Delete(_anchorDir, true);
        }
    }

    private static async Task AppendAsync(string path, string tag)
    {
        using var db = TestDatabase.NewContext(path);
        db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "p", $"{{\"entityId\":\"{tag}\"}}"));
        await LedgerChain.SaveChangesAsync(db);
    }

    private LedgerAnchorService ServiceFor(string dbPath, ILedgerAnchorSink sink)
    {
        var provider = new ServiceCollection()
            .AddDbContext<GovernanceDbContext>(o => o.UseSqlite(TestDatabase.ConnectionString(dbPath)))
            .BuildServiceProvider();
        return new LedgerAnchorService(
            provider.GetRequiredService<IServiceScopeFactory>(), sink, NullLogger<LedgerAnchorService>.Instance,
            new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task EmptyLedger_IsNotAnchored()
    {
        var sink = new FileLedgerAnchorSink(_anchorDir);
        Assert.Null(await ServiceFor(_path, sink).AnchorOnceAsync());
        Assert.Empty(await sink.ReadAllAsync());
    }

    [Fact]
    public async Task Anchor_IsWritten_RecordedInTheLedger_AndNotRepeatedWithoutNewEntries()
    {
        var sink = new FileLedgerAnchorSink(_anchorDir);
        var service = ServiceFor(_path, sink);
        await AppendAsync(_path, "1");

        var first = await service.AnchorOnceAsync();
        Assert.NotNull(first);
        Assert.Equal(2, first!.Sequence); // genesis + 1 entry

        using (var db = TestDatabase.NewContext(_path))
        {
            var recorded = db.GovernanceLedgerEntries.Single(e => e.RitualType == LedgerAnchorService.AnchorRitualType);
            Assert.Equal(3, recorded.Sequence);
            Assert.Contains(first.EntryHash, recorded.ProofJson);
        }

        Assert.Null(await service.AnchorOnceAsync()); // head is the anchor entry: nothing new

        await AppendAsync(_path, "2");
        var second = await service.AnchorOnceAsync();
        Assert.Equal(4, second!.Sequence);
        Assert.Equal(2, (await sink.ReadAllAsync()).Count);

        using var verify = TestDatabase.NewContext(_path);
        Assert.True((await LedgerVerifier.VerifyAsync(verify)).Ok);
        Assert.True((await LedgerVerifier.VerifyAgainstAnchorsAsync(verify, await sink.ReadAllAsync())).Ok);
    }

    [Fact]
    public async Task ARewrittenButSelfConsistentHistory_IsCaughtByTheAnchor()
    {
        var sink = new FileLedgerAnchorSink(_anchorDir);
        await AppendAsync(_path, "honest-1");
        await AppendAsync(_path, "honest-2");
        await ServiceFor(_path, sink).AnchorOnceAsync();

        // An attacker with full database access rebuilds the whole ledger with different content.
        // The result is a perfectly valid chain...
        await AppendAsync(_forgedPath, "FORGED-1");
        await AppendAsync(_forgedPath, "honest-2");
        await AppendAsync(_forgedPath, "padding");

        using var forged = TestDatabase.NewContext(_forgedPath);
        Assert.True((await LedgerVerifier.VerifyAsync(forged)).Ok); // chain verification alone cannot see it

        // ...but it cannot match what was committed outside the database.
        var anchorCheck = await LedgerVerifier.VerifyAgainstAnchorsAsync(forged, await sink.ReadAllAsync());
        Assert.False(anchorCheck.Ok);
        Assert.Contains("history was rewritten", Assert.Single(anchorCheck.Problems));

        // And the anchoring service refuses to bless the forged ledger with a fresh anchor.
        Assert.Null(await ServiceFor(_forgedPath, sink).AnchorOnceAsync());
        Assert.Single(await sink.ReadAllAsync());
    }

    [Fact]
    public async Task TruncatedHistory_IsCaughtByTheAnchor()
    {
        var sink = new FileLedgerAnchorSink(_anchorDir);
        await AppendAsync(_path, "1");
        await ServiceFor(_path, sink).AnchorOnceAsync();

        await AppendAsync(_forgedPath, "only-one"); // shorter ledger than the anchored sequence
        using var shorter = TestDatabase.NewContext(_forgedPath);
        // anchored sequence is 2; shorter ledger has genesis + 1 = seq 2 with a different hash, so use a deeper anchor
        await AppendAsync(_path, "2");
        await AppendAsync(_path, "3");
        await ServiceFor(_path, sink).AnchorOnceAsync();

        var check = await LedgerVerifier.VerifyAgainstAnchorsAsync(shorter, await sink.ReadAllAsync());
        Assert.False(check.Ok);
        Assert.Contains(check.Problems, p => p.Contains("no longer exists"));
    }

    [Fact]
    public async Task FileSink_IsCreateOnly()
    {
        var sink = new FileLedgerAnchorSink(_anchorDir);
        var anchor = new LedgerAnchor(5, new string('c', 64), DateTimeOffset.UtcNow, LedgerHasher.Version, "file");

        await sink.WriteAsync(anchor);
        await sink.WriteAsync(anchor); // identical: idempotent

        var conflicting = anchor with { EntryHash = new string('d', 64) };
        await sink.WriteAsync(conflicting); // different file name (hash is part of it): both are kept, never replaced
        var all = await sink.ReadAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, a => a.EntryHash == anchor.EntryHash);

        // Two anchors for the same sequence with different hashes is itself evidence of tampering.
        await using var db = TestDatabase.NewContext(_path);
        var check = await LedgerVerifier.VerifyAgainstAnchorsAsync(db, all);
        Assert.False(check.Ok);
    }
}

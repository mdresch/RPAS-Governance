using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Exceptions;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0005: ledger hash chain, genesis, tamper detection and immutability (SQLite).</summary>
public sealed class LedgerChainTests : IDisposable
{
    private readonly string _path = TestDatabase.NewPath();

    public LedgerChainTests() => TestDatabase.Create(_path);

    public void Dispose() => TestDatabase.Delete(_path);

    private static GovernanceLedgerEntry Evidence(string ritual = "EvidenceRecorded", string tag = "x") =>
        GovernanceLedgerEntry.CreateHashOnly(ritual, "petitioner-h", $"{{\"entityId\":\"{tag}\"}}");

    private async Task AppendAsync(params GovernanceLedgerEntry[] entries)
    {
        using var db = TestDatabase.NewContext(_path);
        db.GovernanceLedgerEntries.AddRange(entries);
        await LedgerChain.SaveChangesAsync(db);
    }

    private async Task<LedgerVerification> VerifyAsync()
    {
        using var db = TestDatabase.NewContext(_path);
        return await LedgerVerifier.VerifyAsync(db);
    }

    [Fact]
    public async Task EmptyLedger_VerifiesAsEmpty()
    {
        var v = await VerifyAsync();
        Assert.True(v.Ok);
        Assert.Equal(0, v.ChainLength);
        Assert.Null(v.HeadHash);
    }

    [Fact]
    public async Task FirstAppend_CreatesGenesis_AndChainLinks()
    {
        await AppendAsync(Evidence(tag: "1"), Evidence(tag: "2"));

        using var db = TestDatabase.NewContext(_path);
        var rows = db.GovernanceLedgerEntries.OrderBy(e => e.Sequence).ToList();

        Assert.Equal([1L, 2L, 3L], rows.Select(r => r.Sequence!.Value));
        Assert.Equal(LedgerChain.GenesisRitualType, rows[0].RitualType);
        Assert.Equal(LedgerHasher.GenesisPrevHash, rows[0].PrevHash);
        Assert.Equal(rows[0].EntryHash, rows[1].PrevHash);
        Assert.Equal(rows[1].EntryHash, rows[2].PrevHash);
        Assert.All(rows, r => Assert.Equal(64, r.EntryHash!.Length));

        var v = await VerifyAsync();
        Assert.True(v.Ok);
        Assert.Equal(3, v.ChainLength);
        Assert.Equal(rows[2].EntryHash, v.HeadHash);
    }

    [Fact]
    public async Task Genesis_CommitsToLegacyRows()
    {
        // two rows written before the chain existed
        using (var db = TestDatabase.NewContext(_path))
        {
            db.GovernanceLedgerEntries.Add(new GovernanceLedgerEntry("Create", businessCaseJson: "{\"a\":1}"));
            db.GovernanceLedgerEntries.Add(new GovernanceLedgerEntry("MarkApproved", businessCaseJson: "{\"b\":2}"));
            db.SaveChanges();
        }

        await AppendAsync(Evidence());

        using var read = TestDatabase.NewContext(_path);
        var legacy = read.GovernanceLedgerEntries.Where(e => e.Sequence == null).ToList()
            .OrderBy(e => e.Id.ToString("D"), StringComparer.Ordinal).ToList();
        var genesis = read.GovernanceLedgerEntries.Single(e => e.Sequence == 1);

        Assert.Equal(2, legacy.Count);
        Assert.Contains("\"legacyRowCount\":2", genesis.ProofJson);
        Assert.Contains(LedgerHasher.ComputeLegacyDigest(legacy), genesis.ProofJson);

        var v = await VerifyAsync();
        Assert.True(v.Ok);
        Assert.Equal(2, v.UnchainedRows);
    }

    [Fact]
    public async Task ParallelWriters_ProduceOneContiguousChain()
    {
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            await AppendAsync(Evidence(tag: i.ToString()));
        })).ToArray();

        gate.SetResult();
        await Task.WhenAll(tasks);

        var v = await VerifyAsync();
        Assert.True(v.Ok, v.Reason);
        Assert.Equal(17, v.ChainLength); // genesis + 16
    }

    [Fact]
    public async Task MicrosecondPrecision_SurvivesStorage()
    {
        // 7th fractional digit set: must be normalized before hashing or verification would fail after a round trip
        var odd = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        using (var db = TestDatabase.NewContext(_path))
        {
            db.GovernanceLedgerEntries.Add(Evidence());
            await LedgerChain.SaveChangesAsync(db, clock: () => odd);
        }

        var v = await VerifyAsync();
        Assert.True(v.Ok, v.Reason);
    }

    [Fact]
    public async Task DuplicateSequence_IsRejectedByTheDatabase()
    {
        await AppendAsync(Evidence());
        using var db = TestDatabase.NewContext(_path);
        var ex = Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw(
            "INSERT INTO governance_ledger (Id, RitualType, InitiatedAt, Status, IsOverridden, Sequence, PrevHash, EntryHash) " +
            "VALUES ('11111111-1111-1111-1111-111111111111','X','2026-01-01','Completed',0,2,'a','b')"));
        Assert.Contains("UNIQUE", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- tamper detection (SQLite has no triggers, so the rows can be altered directly) ---------------------------

    private void Raw(string sql)
    {
        using var db = TestDatabase.NewContext(_path);
        db.Database.ExecuteSqlRaw(sql);
    }

    [Fact]
    public async Task AlteredContent_IsDetected_AtTheAlteredSequence()
    {
        await AppendAsync(Evidence(tag: "1"), Evidence(tag: "2"), Evidence(tag: "3"));
        Raw("UPDATE governance_ledger SET ProofJson = '{{\"entityId\":\"forged\"}}' WHERE Sequence = 3"); // {{ }}: ExecuteSqlRaw format escaping

        var v = await VerifyAsync();
        Assert.False(v.Ok);
        Assert.Equal(3, v.FirstBrokenSequence);
        Assert.Contains("altered", v.Reason);
    }

    [Fact]
    public async Task DeletedEntry_IsDetected_AsAGap()
    {
        await AppendAsync(Evidence(tag: "1"), Evidence(tag: "2"), Evidence(tag: "3"));
        Raw("DELETE FROM governance_ledger WHERE Sequence = 3");

        var v = await VerifyAsync();
        Assert.False(v.Ok);
        Assert.Equal(3, v.FirstBrokenSequence);
        Assert.Contains("gap", v.Reason);
    }

    [Fact]
    public async Task BrokenLink_IsDetected()
    {
        await AppendAsync(Evidence(tag: "1"), Evidence(tag: "2"));
        Raw("UPDATE governance_ledger SET PrevHash = '" + new string('f', 64) + "' WHERE Sequence = 3");

        var v = await VerifyAsync();
        Assert.False(v.Ok);
        Assert.Equal(3, v.FirstBrokenSequence);
    }

    [Fact]
    public async Task HashWithoutSequence_IsDetected()
    {
        await AppendAsync(Evidence());
        Raw("UPDATE governance_ledger SET Sequence = NULL WHERE Sequence = 2");

        var v = await VerifyAsync();
        Assert.False(v.Ok);
        Assert.Contains("without", v.Reason);
    }

    // ---- immutability ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SealedEntry_RejectsEveryMutationMethod()
    {
        await AppendAsync(Evidence());
        using var db = TestDatabase.NewContext(_path);
        var sealedEntry = db.GovernanceLedgerEntries.First(e => e.Sequence == 2);

        Assert.True(sealedEntry.IsSealed);
        Assert.Equal("LedgerImmutability", Assert.Throws<RpasLawViolationException>(() => sealedEntry.AddGovernorNotes("n")).RuleName);
        Assert.Equal("LedgerImmutability", Assert.Throws<RpasLawViolationException>(() => sealedEntry.OverrideRitual("j")).RuleName);
        Assert.Equal("LedgerImmutability", Assert.Throws<RpasLawViolationException>(() => sealedEntry.MarkInvalidated()).RuleName);
        Assert.Equal("LedgerImmutability", Assert.Throws<RpasLawViolationException>(() => sealedEntry.Attribute("x", "Full")).RuleName);
    }

    [Fact]
    public async Task Interceptor_BlocksModifyAndDelete_OfLedgerEntries()
    {
        await AppendAsync(Evidence());

        using (var db = TestDatabase.NewContext(_path, withLawInterceptor: true))
        {
            var entry = db.GovernanceLedgerEntries.First(e => e.Sequence == 2);
            db.Entry(entry).Property(e => e.ProofJson).CurrentValue = "{}";
            var ex = await Assert.ThrowsAsync<RpasLawViolationException>(() => db.SaveChangesAsync());
            Assert.Equal("LedgerImmutability", ex.RuleName);
        }

        using (var db = TestDatabase.NewContext(_path, withLawInterceptor: true))
        {
            db.GovernanceLedgerEntries.Remove(db.GovernanceLedgerEntries.First(e => e.Sequence == 2));
            var ex = await Assert.ThrowsAsync<RpasLawViolationException>(() => db.SaveChangesAsync());
            Assert.Equal("LedgerAppendOnly", ex.RuleName);
        }

        Assert.True((await VerifyAsync()).Ok);
    }

    [Fact]
    public void Hash_IsDeterministic_AndCoversEveryField()
    {
        GovernanceLedgerEntry Make(Action<GovernanceLedgerEntry>? tweak = null)
        {
            var e = new GovernanceLedgerEntry("Create", "ide", "case").Attribute("p", "Full");
            tweak?.Invoke(e);
            e.Seal(5, new string('a', 64), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            return e;
        }

        // Id and InitiatedAt differ between instances, so compare re-hashing of the SAME sealed instance.
        var one = Make();
        Assert.Equal(one.EntryHash, LedgerHasher.Compute(one));

        // Different prev hash / sequence / content must change the hash.
        var a = new GovernanceLedgerEntry("Create", "ide", "case").Attribute("p", "Full");
        var b = new GovernanceLedgerEntry("Create", "ide", "case").Attribute("p", "Full");
        var when = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        a.Seal(5, new string('a', 64), when);
        b.Seal(5, new string('b', 64), when);
        Assert.NotEqual(a.EntryHash, b.EntryHash);

        var c = new GovernanceLedgerEntry("Create", "ide", "case2").Attribute("p", "Full");
        c.Seal(5, new string('a', 64), when);
        Assert.NotEqual(a.EntryHash, c.EntryHash);
    }
}

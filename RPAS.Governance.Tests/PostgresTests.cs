using Microsoft.EntityFrameworkCore;
using Npgsql;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>
/// Run against a real PostgreSQL (set RPAS_TEST_POSTGRES). They apply the REAL migrations and verify the database-level
/// guarantees that SQLite cannot: the append-only triggers, timestamp precision, the unique chain position, and the
/// atomic single-use token update from AMD-2026-10-01-0001.
/// </summary>
public sealed class PostgresTests : IDisposable
{
    private readonly PostgresDatabase _pg = new();

    public void Dispose() => _pg.Dispose();

    private async Task AppendAsync(params string[] tags)
    {
        using var db = _pg.NewContext();
        foreach (var tag in tags)
        {
            db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "p", $"{{\"entityId\":\"{tag}\"}}"));
        }
        await LedgerChain.SaveChangesAsync(db);
    }

    private static void AssertLedgerRejects(PostgresDatabase pg, string sql)
    {
        var ex = Assert.Throws<PostgresException>(() => pg.Sql(sql));
        Assert.Contains("RPAS-LEDGER", ex.MessageText);
    }

    [PostgresFact]
    public async Task RealMigrations_AndChain_WorkOnPostgres_IncludingTimestampRoundTrip()
    {
        await AppendAsync("1", "2", "3");

        using var db = _pg.NewContext();
        var v = await LedgerVerifier.VerifyAsync(db);
        Assert.True(v.Ok, v.Reason);
        Assert.Equal(4, v.ChainLength);
    }

    [PostgresFact]
    public async Task Triggers_BlockUpdateDeleteAndTruncate_OfChainedRows()
    {
        await AppendAsync("1", "2");

        AssertLedgerRejects(_pg, "UPDATE governance_ledger SET \"Status\" = 'tampered' WHERE \"Sequence\" = 2");
        AssertLedgerRejects(_pg, "UPDATE governance_ledger SET \"ProofJson\" = '{}' WHERE \"Sequence\" = 3");
        AssertLedgerRejects(_pg, "DELETE FROM governance_ledger WHERE \"Sequence\" = 3");
        AssertLedgerRejects(_pg, "TRUNCATE governance_ledger");

        Assert.Equal(3, _pg.Scalar("SELECT count(*) FROM governance_ledger"));
        using var db = _pg.NewContext();
        Assert.True((await LedgerVerifier.VerifyAsync(db)).Ok);
    }

    [PostgresFact]
    public async Task Triggers_FreezeLegacyRows_OnceTheChainExists()
    {
        const string legacy = "INSERT INTO governance_ledger (\"Id\", \"RitualType\", \"InitiatedAt\", \"Status\", \"IsOverridden\") " +
                              "VALUES ('22222222-2222-2222-2222-222222222222', 'Create', now(), 'Completed', false)";
        _pg.Sql(legacy);

        // Before the chain exists, legacy behaviour is unchanged.
        _pg.Sql("UPDATE governance_ledger SET \"Status\" = 'Invalidated' WHERE \"Id\" = '22222222-2222-2222-2222-222222222222'");

        await AppendAsync("1"); // creates the genesis entry: history is now frozen

        AssertLedgerRejects(_pg, "UPDATE governance_ledger SET \"Status\" = 'Completed' WHERE \"Id\" = '22222222-2222-2222-2222-222222222222'");
        AssertLedgerRejects(_pg, "DELETE FROM governance_ledger WHERE \"Id\" = '22222222-2222-2222-2222-222222222222'");

        using var db = _pg.NewContext();
        var v = await LedgerVerifier.VerifyAsync(db);
        Assert.True(v.Ok);
        Assert.Equal(1, v.UnchainedRows);
    }

    [PostgresFact]
    public async Task InsertsStillWork_ForTheApplicationRole()
    {
        // Mirror governance-docs/ledger-append-only-grants.sql with a least-privilege role.
        _pg.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rpas_app_test') THEN CREATE ROLE rpas_app_test LOGIN PASSWORD 'rpas-app-test-only'; END IF; END $$");
        _pg.Sql("GRANT CONNECT ON DATABASE \"" + new NpgsqlConnectionStringBuilder(_pg.ConnectionString).Database + "\" TO rpas_app_test");
        _pg.Sql("GRANT USAGE ON SCHEMA public TO rpas_app_test");
        _pg.Sql("GRANT SELECT, INSERT ON governance_ledger TO rpas_app_test");
        _pg.Sql("REVOKE UPDATE, DELETE, TRUNCATE ON governance_ledger FROM rpas_app_test");

        var appCs = new NpgsqlConnectionStringBuilder(_pg.ConnectionString) { Username = "rpas_app_test", Password = "rpas-app-test-only" }.ConnectionString;
        using var db = new GovernanceDbContext(new DbContextOptionsBuilder<GovernanceDbContext>().UseNpgsql(appCs).Options);
        db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "p", "{\"entityId\":\"x\"}"));
        await LedgerChain.SaveChangesAsync(db);

        // privilege revoked: the database refuses before the trigger is even reached
        var ex = Assert.Throws<PostgresException>(() => new NpgsqlCommand("UPDATE governance_ledger SET \"Status\"='x'", OpenOnce(appCs)).ExecuteNonQuery());
        Assert.Equal("42501", ex.SqlState); // insufficient_privilege

        _pg.Sql("REVOKE ALL ON governance_ledger FROM rpas_app_test");
        _pg.Sql("REVOKE USAGE ON SCHEMA public FROM rpas_app_test");
        _pg.Sql("REVOKE CONNECT ON DATABASE \"" + new NpgsqlConnectionStringBuilder(_pg.ConnectionString).Database + "\" FROM rpas_app_test");
        _pg.Sql("DROP ROLE rpas_app_test");

        static NpgsqlConnection OpenOnce(string cs) { var c = new NpgsqlConnection(cs); c.Open(); return c; }
    }

    [PostgresFact]
    public async Task TwoWritersClaimingTheSameChainPosition_CannotForkTheChain()
    {
        await AppendAsync("1");

        // Simulate two API instances that both read the same head and both try to take sequence 3.
        using var a = _pg.NewContext();
        using var b = _pg.NewContext();
        var head = a.GovernanceLedgerEntries.AsNoTracking().OrderByDescending(e => e.Sequence).First();

        var first = GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "p", "{\"entityId\":\"A\"}");
        var second = GovernanceLedgerEntry.CreateHashOnly("EvidenceRecorded", "p", "{\"entityId\":\"B\"}");
        first.Seal(head.Sequence!.Value + 1, head.EntryHash!, DateTimeOffset.UtcNow);
        second.Seal(head.Sequence!.Value + 1, head.EntryHash!, DateTimeOffset.UtcNow);

        a.GovernanceLedgerEntries.Add(first);
        b.GovernanceLedgerEntries.Add(second);
        await a.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => b.SaveChangesAsync());
        Assert.Equal("23505", ((PostgresException)ex.InnerException!).SqlState); // unique_violation

        using var verify = _pg.NewContext();
        Assert.True((await LedgerVerifier.VerifyAsync(verify)).Ok);
    }

    [PostgresFact]
    public async Task ParallelWriters_ProduceOneChain_OnPostgres()
    {
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 24).Select(i => Task.Run(async () => { await gate.Task; await AppendAsync(i.ToString()); })).ToArray();
        gate.SetResult();
        await Task.WhenAll(tasks);

        using var db = _pg.NewContext();
        var v = await LedgerVerifier.VerifyAsync(db);
        Assert.True(v.Ok, v.Reason);
        Assert.Equal(25, v.ChainLength);
    }

    [PostgresFact]
    public async Task AtomicTokenConsumption_HoldsOnPostgres()
    {
        // Verifies AMD-2026-10-01-0001 against the real database (the earlier tests ran on SQLite).
        Guid id;
        using (var db = _pg.NewContext())
        {
            var token = new AuthorityToken("MarkApproved", "e", 120, "p");
            db.AuthorityTokens.Add(token);
            db.SaveChanges();
            id = token.Id;
        }

        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            using var db = _pg.NewContext();
            return await AuthorityTokenStore.TryConsumeAsync(db, id, DateTime.UtcNow);
        })).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r == TokenConsumeResult.Consumed));
        Assert.Equal(31, results.Count(r => r == TokenConsumeResult.AlreadyConsumed));
    }
}

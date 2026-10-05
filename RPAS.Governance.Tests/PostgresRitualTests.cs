using Microsoft.EntityFrameworkCore;
using Npgsql;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0007 on a real PostgreSQL (set RPAS_TEST_POSTGRES), using the REAL migrations.</summary>
public sealed class PostgresRitualTests : IDisposable
{
    private readonly PostgresDatabase _pg = new();

    public void Dispose() => _pg.Dispose();

    private static void AssertRitualsRejects(PostgresDatabase pg, string sql)
    {
        var ex = Assert.Throws<PostgresException>(() => pg.Sql(sql));
        Assert.Contains("RPAS-RITUALS", ex.MessageText);
    }

    [PostgresFact]
    public async Task Seeding_WritesTheDefinitionsAndOneLedgerEvent_AndRoundTripsThroughJsonb()
    {
        using (var db = _pg.NewContext())
        {
            Assert.NotNull(await RitualDefinitionStore.ResolveAsync(db, "sidpa", "Heal"));
        }

        using var read = _pg.NewContext();
        var stored = await read.RitualDefinitions.AsNoTracking().ToListAsync();
        Assert.Equal(RitualSeed.Definitions.Count, stored.Count);

        var heal = stored.Single(d => d.RitualType == "Heal");
        Assert.Equal(RitualScopes.Heal, heal.Scope);
        Assert.Equal(["/governed/implementation/*"], heal.AllowedPaths);

        // The stored hash is reproducible from the stored fields: what an auditor can recompute is what was recorded.
        foreach (var d in stored)
        {
            var recomputed = RitualDefinition.Create(d.PetitionerId, d.RitualType, d.Version, d.IsRetired, d.AcceptsEvidence, d.MetadataKeys, d.Scope, d.AllowedPaths);
            Assert.Equal(d.DefinitionHash, recomputed.DefinitionHash);
        }

        var seeds = await read.GovernanceLedgerEntries.AsNoTracking().Where(e => e.RitualType == RitualDefinitionStore.ChangeRitualType).ToListAsync();
        Assert.Single(seeds);
    }

    [PostgresFact]
    public async Task ConcurrentFirstUse_SeedsExactlyOnce()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            using var db = _pg.NewContext();
            Assert.NotNull(await RitualDefinitionStore.ResolveAsync(db, $"p{i}", "Implement"));
        }));

        Assert.Equal(RitualSeed.Definitions.Count, _pg.Scalar("SELECT count(*) FROM ritual_definitions"));
        Assert.Equal(1, _pg.Scalar($"SELECT count(*) FROM governance_ledger WHERE \"RitualType\" = '{RitualDefinitionStore.ChangeRitualType}'"));
    }

    [PostgresFact]
    public async Task DefinitionRows_AreAppendOnly_UpdateDeleteAndTruncateAreRefused()
    {
        using (var db = _pg.NewContext())
        {
            await RitualDefinitionStore.ResolveAsync(db, "sidpa", "Heal");
        }

        AssertRitualsRejects(_pg, "UPDATE ritual_definitions SET \"AllowedPaths\" = '[\"/governed/contracts/*\"]' WHERE \"RitualType\" = 'Heal'");
        AssertRitualsRejects(_pg, "DELETE FROM ritual_definitions WHERE \"RitualType\" = 'Heal'");
        AssertRitualsRejects(_pg, "TRUNCATE ritual_definitions");

        // Even a superuser-equivalent role with full privileges cannot widen the heal envelope behind the application's back.
        Assert.Equal(1, _pg.Scalar("SELECT count(*) FROM ritual_definitions WHERE \"RitualType\" = 'Heal' AND \"AllowedPaths\"::text NOT LIKE '%contracts%'"));
    }

    [PostgresFact]
    public async Task APublishedVersion_IsANewRow_AndTheOldOneIsUntouched()
    {
        using var db = _pg.NewContext();
        var v2 = await RitualDefinitionStore.PublishAsync(
            db, "*", "Heal", isRetired: false, acceptsEvidence: false, metadataKeys: null,
            scope: RitualScopes.Heal, allowedPaths: ["/governed/implementation/*", "/governed/healing/*"],
            changedByHumanId: "human-1", viaPetitionerId: "sidpa");

        Assert.Equal(2, v2.Version);
        Assert.Equal(2, _pg.Scalar("SELECT count(*) FROM ritual_definitions WHERE \"RitualType\" = 'Heal'"));
        Assert.Equal(1, _pg.Scalar("SELECT count(*) FROM ritual_definitions WHERE \"RitualType\" = 'Heal' AND \"Version\" = 1 AND \"AllowedPaths\"::text NOT LIKE '%healing%'"));
        Assert.Equal(2, _pg.Scalar($"SELECT count(*) FROM governance_ledger WHERE \"RitualType\" = '{RitualDefinitionStore.ChangeRitualType}'")); // seed + publish
    }

    [PostgresFact]
    public async Task AVersionNumber_CannotBeUsedTwice()
    {
        using (var db = _pg.NewContext())
        {
            await RitualDefinitionStore.ResolveAsync(db, "sidpa", "Heal");
        }

        var ex = Assert.Throws<PostgresException>(() => _pg.Sql(
            "INSERT INTO ritual_definitions (\"Id\", \"PetitionerId\", \"RitualType\", \"Version\", \"IsRetired\", \"AcceptsEvidence\", \"MetadataKeys\", \"AllowedPaths\", \"DefinitionHash\", \"CreatedAt\") " +
            "VALUES (gen_random_uuid(), '*', 'Heal', 1, false, false, '[]', '[]', 'x', now())"));
        Assert.Equal("23505", ex.SqlState); // unique_violation
    }

    [PostgresFact]
    public async Task TokenScope_AndHumanId_RoundTrip()
    {
        Guid id;
        using (var db = _pg.NewContext())
        {
            var token = new AuthorityToken("EditContract", "suite-1", 120, "sidpa", RitualScopes.EditContract, "11111111-1111-1111-1111-111111111111");
            token.AllowedPaths.Add("/governed/contracts/*");
            db.AuthorityTokens.Add(token);
            await db.SaveChangesAsync();
            id = token.Id;
        }

        using var read = _pg.NewContext();
        var loaded = await read.AuthorityTokens.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(RitualScopes.EditContract, loaded.Scope);
        Assert.Equal("11111111-1111-1111-1111-111111111111", loaded.HumanId);
    }
}

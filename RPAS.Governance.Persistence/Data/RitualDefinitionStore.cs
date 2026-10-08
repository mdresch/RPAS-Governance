using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Persistence.Data;

/// <summary>Raised when a ritual definition cannot be published (invalid, or another publisher took the version).</summary>
public sealed class RitualDefinitionException(string message) : Exception(message);

/// <summary>
/// Reads and publishes versioned ritual definitions (AMD-2026-10-01-0007).
///
/// A definition change and its ledger event are saved in the SAME SaveChanges, so a definition can never exist
/// without the ledger entry that records who published it and what its hash is.
/// </summary>
public static class RitualDefinitionStore
{
    public const string ChangeRitualType = "RitualDefinitionChanged";

    /// <summary>
    /// Inserts the seed definitions and one ledger event, once, when the store is empty. Safe to call on every request:
    /// when two callers race, the unique index lets one win and the other finds the rows already there.
    /// </summary>
    public static async Task EnsureSeededAsync(GovernanceDbContext db, CancellationToken ct = default)
    {
        if (await db.RitualDefinitions.AsNoTracking().AnyAsync(ct))
        {
            return;
        }

        var seeds = RitualSeed.Definitions
            .Select(d => RitualDefinition.Create(d.PetitionerId, d.RitualType, d.Version, d.IsRetired, d.AcceptsEvidence, d.MetadataKeys, d.Scope, d.AllowedPaths))
            .ToList();

        var proof = JsonSerializer.Serialize(new
        {
            action = "seed",
            definitionCount = seeds.Count,
            definitions = seeds.Select(d => new { d.PetitionerId, d.RitualType, d.Version, d.DefinitionHash })
        });

        var seedEntry = GovernanceLedgerEntry.CreateHashOnly(ChangeRitualType, "rpas-system", proof);
        db.RitualDefinitions.AddRange(seeds);
        db.GovernanceLedgerEntries.Add(seedEntry);

        try
        {
            await LedgerChain.SaveChangesAsync(db, ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or LedgerContentionException)
        {
            // Another caller seeded first. Detach only our own pending rows (the caller may have others pending)
            // and carry on if the store is now populated.
            foreach (var seed in seeds) { db.Entry(seed).State = EntityState.Detached; }
            db.Entry(seedEntry).State = EntityState.Detached;
            if (!await db.RitualDefinitions.AsNoTracking().AnyAsync(ct))
            {
                throw;
            }
        }
    }

    /// <summary>
    /// The definition in force for a petitioner: its own latest version when it has one, otherwise the default's.
    /// A retired latest version means the ritual is unavailable (null), with no fall back to the default.
    /// </summary>
    public static async Task<RitualDefinition?> ResolveAsync(
        GovernanceDbContext db, string petitionerId, string? ritualType, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ritualType))
        {
            return null;
        }

        await EnsureSeededAsync(db, ct);

        var rows = await db.RitualDefinitions.AsNoTracking()
            .Where(d => d.RitualType == ritualType && (d.PetitionerId == petitionerId || d.PetitionerId == RitualDefinition.DefaultPetitioner))
            .ToListAsync(ct);

        var latest = rows.Where(d => d.PetitionerId == petitionerId).OrderByDescending(d => d.Version).FirstOrDefault()
                     ?? rows.Where(d => d.PetitionerId == RitualDefinition.DefaultPetitioner).OrderByDescending(d => d.Version).FirstOrDefault();

        return latest is null || latest.IsRetired ? null : latest;
    }

    /// <summary>All definitions in force for a petitioner (latest version per ritual, retired ones omitted).</summary>
    public static async Task<IReadOnlyList<RitualDefinition>> ListInForceAsync(
        GovernanceDbContext db, string petitionerId, CancellationToken ct = default)
    {
        await EnsureSeededAsync(db, ct);

        var rows = await db.RitualDefinitions.AsNoTracking()
            .Where(d => d.PetitionerId == petitionerId || d.PetitionerId == RitualDefinition.DefaultPetitioner)
            .ToListAsync(ct);

        return rows
            .GroupBy(d => d.RitualType, StringComparer.Ordinal)
            .Select(g => g.Where(d => d.PetitionerId == petitionerId).OrderByDescending(d => d.Version).FirstOrDefault()
                         ?? g.OrderByDescending(d => d.Version).First())
            .Where(d => !d.IsRetired)
            .OrderBy(d => d.RitualType, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Publishes the next version of a ritual definition for <paramref name="petitionerId"/> ("*" for the default)
    /// and records the change on the ledger, attributed to the named human who made it and the petitioner they used.
    /// </summary>
    public static async Task<RitualDefinition> PublishAsync(
        GovernanceDbContext db,
        string petitionerId,
        string ritualType,
        bool isRetired,
        bool acceptsEvidence,
        IEnumerable<string>? metadataKeys,
        string? scope,
        IEnumerable<string>? allowedPaths,
        string changedByHumanId,
        string viaPetitionerId,
        CancellationToken ct = default)
    {
        await EnsureSeededAsync(db, ct);

        var currentMax = await db.RitualDefinitions.AsNoTracking()
            .Where(d => d.PetitionerId == petitionerId && d.RitualType == ritualType)
            .Select(d => (int?)d.Version)
            .MaxAsync(ct);

        // A petitioner-specific ritual starts from the default's version so the history reads as one line.
        var baseline = currentMax ?? await db.RitualDefinitions.AsNoTracking()
            .Where(d => d.PetitionerId == RitualDefinition.DefaultPetitioner && d.RitualType == ritualType)
            .Select(d => (int?)d.Version)
            .MaxAsync(ct) ?? 0;

        var definition = RitualDefinition.Create(
            petitionerId, ritualType, baseline + 1, isRetired, acceptsEvidence, metadataKeys, scope, allowedPaths);

        var problem = definition.Validate();
        if (problem is not null)
        {
            throw new RitualDefinitionException(problem);
        }

        var proof = JsonSerializer.Serialize(new
        {
            action = isRetired ? "retire" : "publish",
            petitionerId = definition.PetitionerId,
            ritualType = definition.RitualType,
            version = definition.Version,
            definitionHash = definition.DefinitionHash,
            changedBy = changedByHumanId,
            viaPetitioner = viaPetitionerId
        });

        db.RitualDefinitions.Add(definition);
        db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly(ChangeRitualType, viaPetitionerId, proof));

        try
        {
            await LedgerChain.SaveChangesAsync(db, ct);
        }
        catch (DbUpdateException)
        {
            var taken = await db.RitualDefinitions.AsNoTracking().AnyAsync(
                d => d.PetitionerId == petitionerId && d.RitualType == ritualType && d.Version == definition.Version && d.Id != definition.Id, CancellationToken.None);
            if (taken)
            {
                throw new RitualDefinitionException("Another publisher took this version first; retry the request.");
            }
            throw;
        }

        return definition;
    }
}

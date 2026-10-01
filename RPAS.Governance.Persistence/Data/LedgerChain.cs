using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Persistence.Data;

/// <summary>Raised when another writer took the next chain position first. The caller should retry the request.</summary>
public sealed class LedgerContentionException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Appends entries to the ledger hash chain (AMD-2026-10-01-0005).
///
/// All sealing happens under one process-wide lock, so a single API instance can never fork the chain.
/// Across instances the unique index on "Sequence" is the guarantee: a second writer that computed the same
/// position fails with <see cref="LedgerContentionException"/> instead of forking the chain.
/// New entries are saved in the SAME SaveChanges as the mutation that caused them, so audit and mutation stay atomic.
/// </summary>
public static class LedgerChain
{
    public const string GenesisRitualType = "ChainGenesis";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>True once the genesis entry exists. From then on history is frozen: no ledger row may change.</summary>
    public static Task<bool> IsActiveAsync(GovernanceDbContext db, CancellationToken ct = default) =>
        db.GovernanceLedgerEntries.AsNoTracking().AnyAsync(e => e.Sequence == 1, ct);

    public static async Task<int> SaveChangesAsync(
        GovernanceDbContext db,
        CancellationToken ct = default,
        Func<DateTimeOffset>? clock = null)
    {
        clock ??= () => DateTimeOffset.UtcNow;

        var pending = db.ChangeTracker.Entries<GovernanceLedgerEntry>()
            .Where(e => e.State == EntityState.Added && !e.Entity.IsSealed)
            .Select(e => e.Entity)
            .ToList();

        if (pending.Count == 0)
        {
            return await db.SaveChangesAsync(ct);
        }

        await Gate.WaitAsync(ct);
        try
        {
            var head = await db.GovernanceLedgerEntries.AsNoTracking()
                .Where(e => e.Sequence != null)
                .OrderByDescending(e => e.Sequence)
                .Select(e => new { e.Sequence, e.EntryHash })
                .FirstOrDefaultAsync(ct);

            long nextSequence;
            string prevHash;

            if (head is null)
            {
                var genesis = await CreateGenesisAsync(db, ct);
                genesis.Seal(1, LedgerHasher.GenesisPrevHash, clock());
                db.GovernanceLedgerEntries.Add(genesis);
                nextSequence = 2;
                prevHash = genesis.EntryHash!;
            }
            else
            {
                nextSequence = head.Sequence!.Value + 1;
                prevHash = head.EntryHash!;
            }

            var firstSequence = nextSequence;
            foreach (var entry in pending)
            {
                entry.Seal(nextSequence++, prevHash, clock());
                prevHash = entry.EntryHash!;
            }

            try
            {
                return await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                if (await SequenceTakenAsync(firstSequence, pending[0].Id))
                {
                    throw new LedgerContentionException("Another writer appended to the ledger first; retry the request.", ex);
                }
                throw;
            }
        }
        finally
        {
            Gate.Release();
        }

        // After a failed save the context is still usable for a no-tracking read.
        async Task<bool> SequenceTakenAsync(long sequence, Guid ownEntryId) =>
            await db.GovernanceLedgerEntries.AsNoTracking().AnyAsync(e => e.Sequence == sequence && e.Id != ownEntryId, CancellationToken.None);
    }

    private static async Task<GovernanceLedgerEntry> CreateGenesisAsync(GovernanceDbContext db, CancellationToken ct)
    {
        // Ordered client-side by Id (lowercase "D" text, ordinal) so the order is identical on every database
        // provider and an auditor can reproduce the digest.
        var legacy = (await db.GovernanceLedgerEntries.AsNoTracking()
                .Where(e => e.Sequence == null)
                .ToListAsync(ct))
            .OrderBy(e => e.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();

        var proof = JsonSerializer.Serialize(new
        {
            chainVersion = LedgerHasher.Version,
            legacyRowCount = legacy.Count,
            legacyDigest = LedgerHasher.ComputeLegacyDigest(legacy)
        });

        return GovernanceLedgerEntry.CreateHashOnly(GenesisRitualType, "rpas-system", proof);
    }
}

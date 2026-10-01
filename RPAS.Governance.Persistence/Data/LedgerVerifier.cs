using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Persistence.Data;

/// <summary>An externally stored commitment to the chain head at a point in time.</summary>
public sealed record LedgerAnchor(long Sequence, string EntryHash, DateTimeOffset CreatedAt, string ChainVersion, string? Reference = null);

/// <summary>Where anchors are written. Implementations must be create-only: an existing anchor is never replaced.</summary>
public interface ILedgerAnchorSink
{
    string Name { get; }
    Task WriteAsync(LedgerAnchor anchor, CancellationToken ct = default);
    Task<IReadOnlyList<LedgerAnchor>> ReadAllAsync(CancellationToken ct = default);
}

public sealed record LedgerVerification(
    bool Ok,
    long ChainLength,
    long? HeadSequence,
    string? HeadHash,
    long? FirstBrokenSequence,
    string? Reason,
    long UnchainedRows);

public sealed record AnchorVerification(
    bool Ok,
    int AnchorsChecked,
    long? LatestAnchoredSequence,
    IReadOnlyList<string> Problems);

/// <summary>Recomputes the hash chain from the stored rows (AMD-2026-10-01-0005).</summary>
public static class LedgerVerifier
{
    public static async Task<LedgerVerification> VerifyAsync(GovernanceDbContext db, CancellationToken ct = default)
    {
        var unchained = await db.GovernanceLedgerEntries.AsNoTracking().LongCountAsync(e => e.Sequence == null && e.EntryHash == null, ct);

        long expected = 1;
        var previousHash = LedgerHasher.GenesisPrevHash;
        long length = 0;
        string? headHash = null;

        // A hash without a sequence (or the reverse) can only come from tampering.
        var orphan = await db.GovernanceLedgerEntries.AsNoTracking()
            .Where(e => (e.Sequence == null) != (e.EntryHash == null))
            .Select(e => e.Id).FirstOrDefaultAsync(ct);
        if (orphan != Guid.Empty)
        {
            return new LedgerVerification(false, 0, null, null, null, $"Entry {orphan} has a sequence without a hash or a hash without a sequence.", unchained);
        }

        await foreach (var e in db.GovernanceLedgerEntries.AsNoTracking()
                           .Where(e => e.Sequence != null)
                           .OrderBy(e => e.Sequence)
                           .AsAsyncEnumerable().WithCancellation(ct))
        {
            if (e.Sequence != expected)
            {
                return Broken(expected, $"Expected sequence {expected} but found {e.Sequence} (gap, duplicate or removed entry).");
            }
            if (!string.Equals(e.PrevHash, previousHash, StringComparison.Ordinal))
            {
                return Broken(e.Sequence.Value, "PrevHash does not match the hash of the previous entry.");
            }

            var recomputed = LedgerHasher.Compute(e);
            if (!string.Equals(recomputed, e.EntryHash, StringComparison.Ordinal))
            {
                return Broken(e.Sequence.Value, "EntryHash does not match the entry's content (entry was altered).");
            }

            previousHash = e.EntryHash!;
            headHash = e.EntryHash;
            expected++;
            length++;
        }

        return new LedgerVerification(true, length, length == 0 ? null : length, headHash, null, null, unchained);

        LedgerVerification Broken(long sequence, string reason) =>
            new(false, length, length == 0 ? null : length, headHash, sequence, reason, unchained);
    }

    /// <summary>Checks that every externally stored anchor still matches the chain entry it committed to.</summary>
    public static async Task<AnchorVerification> VerifyAgainstAnchorsAsync(
        GovernanceDbContext db, IEnumerable<LedgerAnchor> anchors, CancellationToken ct = default)
    {
        var problems = new List<string>();
        var checkedCount = 0;
        long? latest = null;

        foreach (var anchor in anchors.OrderBy(a => a.Sequence))
        {
            checkedCount++;
            latest = anchor.Sequence;

            var hash = await db.GovernanceLedgerEntries.AsNoTracking()
                .Where(e => e.Sequence == anchor.Sequence)
                .Select(e => e.EntryHash)
                .FirstOrDefaultAsync(ct);

            if (hash is null)
            {
                problems.Add($"Anchor for sequence {anchor.Sequence}: the entry no longer exists.");
            }
            else if (!string.Equals(hash, anchor.EntryHash, StringComparison.Ordinal))
            {
                problems.Add($"Anchor for sequence {anchor.Sequence}: the entry hash differs from the anchored hash (history was rewritten).");
            }
        }

        return new AnchorVerification(problems.Count == 0, checkedCount, latest, problems);
    }
}

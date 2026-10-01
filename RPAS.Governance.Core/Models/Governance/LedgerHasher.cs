using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// Canonical hashing for the ledger hash chain (AMD-2026-10-01-0005).
///
/// Every field is written as a 4-byte big-endian length followed by its UTF-8 bytes (length -1 for null),
/// so no two different entries can serialize to the same byte stream. The field order is part of the
/// format: changing it requires a new <see cref="Version"/>.
/// </summary>
public static class LedgerHasher
{
    public const string Version = "RPAS-LEDGER-V1";

    /// <summary>PrevHash of the genesis entry.</summary>
    public static readonly string GenesisPrevHash = new('0', 64);

    public static string Compute(GovernanceLedgerEntry e)
    {
        var w = new CanonicalWriter();
        w.Str(Version);
        w.Str(e.Sequence?.ToString(CultureInfo.InvariantCulture));
        w.Str(e.PrevHash);
        w.Str(e.Id.ToString("D"));
        w.Str(e.RitualType);
        w.Str(FormatTime(e.InitiatedAt));
        w.Str(e.Status);
        w.Str(e.IdeationJson);
        w.Str(e.BusinessCaseJson);
        w.Str(e.GovernorNotes);
        w.Str(e.IsOverridden ? "1" : "0");
        w.Str(e.OverrideJustification);
        w.Str(e.PetitionerId);
        w.Str(e.RefersToEntryId?.ToString("D"));
        w.Str(e.ContentMode);
        w.Str(e.ProofJson);
        return w.Finish();
    }

    /// <summary>
    /// Commitment to the pre-chain ("legacy") rows, recorded in the genesis entry. It fixes what history
    /// looked like at the moment the chain started.
    /// </summary>
    public static string ComputeLegacyDigest(IEnumerable<GovernanceLedgerEntry> legacyRowsInOrder)
    {
        var w = new CanonicalWriter();
        w.Str(Version + "/legacy");
        foreach (var e in legacyRowsInOrder)
        {
            w.Str(e.Id.ToString("D"));
            w.Str(e.RitualType);
            w.Str(FormatTime(e.InitiatedAt));
            w.Str(e.Status);
            w.Str(e.IdeationJson);
            w.Str(e.BusinessCaseJson);
            w.Str(e.GovernorNotes);
            w.Str(e.IsOverridden ? "1" : "0");
            w.Str(e.OverrideJustification);
        }
        return w.Finish();
    }

    private static string FormatTime(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private sealed class CanonicalWriter
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void Str(string? value)
        {
            if (value is null)
            {
                Int(-1);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            Int(bytes.Length);
            _hash.AppendData(bytes);
        }

        public string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        private void Int(int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            _hash.AppendData(buffer);
        }
    }
}

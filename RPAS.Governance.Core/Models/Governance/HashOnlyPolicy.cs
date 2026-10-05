using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// What a hash-only petitioner may send (AMD-2026-10-01-0006). The courthouse receives a content hash plus a
/// small allow-list of identifier-style metadata, never document content. Which rituals exist and which metadata
/// keys each allows is data (AMD-2026-10-01-0007): the caller resolves the ritual definition and passes its keys in. Anything else is rejected, so personal
/// or confidential data cannot reach the ledger by accident.
///
/// Limits of this control: it constrains the SHAPE of values (short identifier-like tokens: no spaces, no '@',
/// no free text, no nesting). It cannot prove that an identifier is not personal data, so petitioners must send
/// pseudonymous references (e.g. "reviewerRef"), never names or e-mail addresses.
/// </summary>
public static partial class HashOnlyPolicy
{
    public const int MaxMetadataEntries = 16;

    /// <summary>hash algorithm -> expected lowercase-hex length of the content hash.</summary>
    public static readonly IReadOnlyDictionary<string, int> HashAlgorithms = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["SHA-256"] = 64,
        ["SHA-512"] = 128,
        ["HMAC-SHA-256"] = 64,
        ["HMAC-SHA-512"] = 128,
    };

    [GeneratedRegex("^[A-Za-z0-9._:/+-]{1,128}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[0-9a-f]+$")]
    private static partial Regex LowerHexPattern();

    public static bool IsIdentifier(string? value) => value is not null && IdentifierPattern().IsMatch(value);

    /// <summary>
    /// Validates the request shape. Returns null when acceptable, otherwise a message that names the offending
    /// field but never echoes a submitted value.
    /// </summary>
    public static string? Validate(
        string? ritualType,
        IReadOnlyCollection<string>? allowedKeys,
        string? entityId,
        string? contentHash,
        string? hashAlgorithm,
        string? keyId,
        IReadOnlyDictionary<string, object?>? metadata)
    {
        if (ritualType is null || allowedKeys is null)
        {
            return "ritualType is not an allowed hash-only ritual type.";
        }
        if (!IsIdentifier(entityId))
        {
            return "entityId must be an identifier (letters, digits and . _ : / + - only, 1-128 characters).";
        }
        if (hashAlgorithm is null || !HashAlgorithms.TryGetValue(hashAlgorithm, out var hexLength))
        {
            return "hashAlgorithm must be one of: " + string.Join(", ", HashAlgorithms.Keys) + ".";
        }
        if (contentHash is null || contentHash.Length != hexLength || !LowerHexPattern().IsMatch(contentHash))
        {
            return $"contentHash must be {hexLength} lowercase hexadecimal characters for {hashAlgorithm}.";
        }
        if (hashAlgorithm.StartsWith("HMAC", StringComparison.Ordinal))
        {
            if (!IsIdentifier(keyId))
            {
                return "keyId (an identifier for the HMAC key, never the key itself) is required for HMAC algorithms.";
            }
        }
        else if (keyId is not null)
        {
            return "keyId is only valid with an HMAC algorithm.";
        }

        if (metadata is not null)
        {
            if (metadata.Count > MaxMetadataEntries)
            {
                return $"metadata may contain at most {MaxMetadataEntries} entries.";
            }

            foreach (var (key, value) in metadata)
            {
                if (!allowedKeys.Contains(key))
                {
                    return $"metadata key '{(IsIdentifier(key) ? key : "<invalid>")}' is not allowed for ritual '{ritualType}'.";
                }

                var valueOk = value switch
                {
                    string s => IsIdentifier(s),
                    bool => true,
                    int i => i >= 0,
                    long l => l >= 0 && l < int.MaxValue,
                    _ => false,
                };
                if (!valueOk)
                {
                    return $"metadata value for '{key}' must be an identifier-style string, a boolean or a non-negative integer.";
                }
            }
        }

        return null;
    }
}

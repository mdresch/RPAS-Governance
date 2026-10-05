using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// A ritual type stored as versioned data (AMD-2026-10-01-0007). Replaces the static envelope dictionary and the
/// static hash-only allow-lists.
///
/// Versions are immutable: a change is a NEW row with the next version number, and the change is recorded as a
/// ledger event. A retired version means the ritual is no longer available to that petitioner (fail closed, there
/// is no fall back to the default definition).
/// </summary>
public class RitualDefinition
{
    /// <summary>PetitionerId that marks the default definition, used when a petitioner has none of its own.</summary>
    public const string DefaultPetitioner = "*";

    public Guid Id { get; init; } = Guid.NewGuid();
    public string PetitionerId { get; init; } = DefaultPetitioner;
    public string RitualType { get; init; } = string.Empty;
    public int Version { get; init; }
    public bool IsRetired { get; init; }

    /// <summary>True when the ritual is recorded as hash-only evidence through POST /Evidence/record.</summary>
    public bool AcceptsEvidence { get; init; }

    /// <summary>Metadata keys that may accompany evidence for this ritual.</summary>
    public List<string> MetadataKeys { get; init; } = new();

    /// <summary>Token scope required to hold a token for this ritual, or null when the ritual issues no scoped token.</summary>
    public string? Scope { get; init; }

    /// <summary>Path envelope granted to a token issued for this ritual.</summary>
    public List<string> AllowedPaths { get; init; } = new();

    /// <summary>SHA-256 over the canonical form of this definition; recorded in the ledger event for the change.</summary>
    public string DefinitionHash { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    protected RitualDefinition() { }

    public static RitualDefinition Create(
        string petitionerId,
        string ritualType,
        int version,
        bool isRetired,
        bool acceptsEvidence,
        IEnumerable<string>? metadataKeys,
        string? scope,
        IEnumerable<string>? allowedPaths)
    {
        var keys = (metadataKeys ?? []).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var paths = (allowedPaths ?? []).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
        return new RitualDefinition
        {
            PetitionerId = petitionerId,
            RitualType = ritualType,
            Version = version,
            IsRetired = isRetired,
            AcceptsEvidence = acceptsEvidence,
            MetadataKeys = keys,
            Scope = scope,
            AllowedPaths = paths,
            DefinitionHash = ComputeHash(petitionerId, ritualType, version, isRetired, acceptsEvidence, keys, scope, paths),
            CreatedAt = new DateTimeOffset(DateTime.UtcNow.Ticks - DateTime.UtcNow.Ticks % 10, TimeSpan.Zero)
        };
    }

    /// <summary>
    /// Validates the structure of a definition. Returns null when acceptable, otherwise a message that names the
    /// problem. Enforced when a definition is published, so an invalid or over-broad definition never reaches the store.
    /// </summary>
    public string? Validate()
    {
        if (!HashOnlyPolicy.IsIdentifier(RitualType))
        {
            return "ritualType must be an identifier (letters, digits and . _ : / + - only, 1-128 characters).";
        }
        if (PetitionerId != DefaultPetitioner && !HashOnlyPolicy.IsIdentifier(PetitionerId))
        {
            return "petitionerId must be '*' or an identifier.";
        }
        if (Version < 1)
        {
            return "version must be 1 or greater.";
        }
        if (Scope is not null && !RitualScopes.IsKnown(Scope))
        {
            return "scope must be one of: " + string.Join(", ", RitualScopes.All) + ".";
        }
        if (MetadataKeys.Count > HashOnlyPolicy.MaxMetadataEntries || MetadataKeys.Any(k => !HashOnlyPolicy.IsIdentifier(k)))
        {
            return $"metadataKeys must be at most {HashOnlyPolicy.MaxMetadataEntries} identifiers.";
        }
        if (!AcceptsEvidence && MetadataKeys.Count > 0)
        {
            return "metadataKeys are only valid for a ritual that accepts evidence.";
        }
        if (IsRetired)
        {
            return null; // a retirement carries no grants to check
        }
        foreach (var pattern in AllowedPaths)
        {
            if (!IsValidPattern(pattern))
            {
                return "allowedPaths entries must be canonical absolute paths: '/dir/*' or an exact '/dir/file'.";
            }
            var owner = RitualScopes.ReservedOwnerOf(pattern);
            if (owner is not null && !string.Equals(owner, Scope, StringComparison.Ordinal))
            {
                return $"path '{pattern}' is in an area reserved for scope '{owner}'.";
            }
        }
        if (Scope is not null && AllowedPaths.Count == 0)
        {
            return "a ritual with a token scope must grant at least one allowed path.";
        }
        return null;
    }

    private static bool IsValidPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern.Length > 256)
        {
            return false;
        }
        var dir = pattern.EndsWith("/*", StringComparison.Ordinal) ? pattern[..^2] : pattern;
        if (dir.Contains('*') || !PathEnvelope.TryCanonicalize(dir.Length == 0 ? "/" : dir, out var canonical))
        {
            return false;
        }
        // Only canonical input is accepted, so what is reviewed is what is enforced.
        return canonical == (dir.Length == 0 ? "/" : dir);
    }

    private static string ComputeHash(
        string petitionerId, string ritualType, int version, bool isRetired, bool acceptsEvidence,
        IReadOnlyList<string> metadataKeys, string? scope, IReadOnlyList<string> allowedPaths)
    {
        using var stream = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("petitionerId", petitionerId);
            w.WriteString("ritualType", ritualType);
            w.WriteNumber("version", version);
            w.WriteBoolean("isRetired", isRetired);
            w.WriteBoolean("acceptsEvidence", acceptsEvidence);
            w.WriteStartArray("metadataKeys");
            foreach (var k in metadataKeys) { w.WriteStringValue(k); }
            w.WriteEndArray();
            if (scope is null) { w.WriteNull("scope"); } else { w.WriteString("scope", scope); }
            w.WriteStartArray("allowedPaths");
            foreach (var p in allowedPaths) { w.WriteStringValue(p); }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}

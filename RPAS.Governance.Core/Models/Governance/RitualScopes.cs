using System;
using System.Collections.Generic;

namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// Token scopes for SIDPA (AMD-2026-10-01-0007). A scope says WHAT a token may be used to do; the ritual
/// definition's path envelope says WHERE. The two are tied together structurally: the reserved path areas below
/// can only be granted to the scope that owns them, so a heal token can never be given contract or intent paths,
/// not even by a later definition change.
/// </summary>
public static class RitualScopes
{
    public const string DeclareIntent = "declare-intent";
    public const string Implement = "implement";
    public const string Heal = "heal";
    public const string EditContract = "edit-contract";

    public static readonly IReadOnlyList<string> All = [DeclareIntent, Implement, Heal, EditContract];

    /// <summary>Path areas that only one scope may ever be granted.</summary>
    public static readonly IReadOnlyDictionary<string, string> ReservedPathPrefixes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["/governed/intent"] = DeclareIntent,
        ["/governed/contracts"] = EditContract,
    };

    public static bool IsKnown(string? scope) => scope is not null && All.Contains(scope);

    /// <summary>Scopes that are issued to a named human only, never to an automated petitioner.</summary>
    public static bool IsHumanOnly(string? scope) => string.Equals(scope, EditContract, StringComparison.Ordinal);

    /// <summary>Returns the scope that owns <paramref name="pattern"/>'s area, or null when the area is not reserved.</summary>
    public static string? ReservedOwnerOf(string pattern)
    {
        var dir = pattern.EndsWith("/*", StringComparison.Ordinal) ? pattern[..^2] : pattern;
        if (!PathEnvelope.TryCanonicalize(dir, out var canonical))
        {
            return null;
        }

        foreach (var (prefix, owner) in ReservedPathPrefixes)
        {
            if (canonical == prefix || canonical.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return owner;
            }
        }
        return null;
    }
}

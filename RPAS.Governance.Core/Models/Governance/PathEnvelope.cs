using System;
using System.Collections.Generic;
using System.Text;

namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// G6 topology check (AMD-2026-10-01-0002).
///
/// Paths are canonicalized BEFORE they are compared with an envelope, and the comparison happens
/// on path-segment boundaries. The previous implementation used a raw string StartsWith, which let
/// "/docs/ratified/../x" and "/docs/ratified-evil/x" pass the "/docs/ratified/*" envelope.
///
/// The check is deliberately strict and fails closed: anything that cannot be canonicalized
/// unambiguously is rejected rather than interpreted.
/// </summary>
public static class PathEnvelope
{
    /// <summary>
    /// Canonicalizes a path to the form "/a/b/c". Returns false (and a null canonical path) when the
    /// path is not acceptable.
    ///
    /// Rejected: null/empty/relative paths, backslashes, '%' (percent-encoding is never decoded, so
    /// an encoded "../" can never be smuggled through to a downstream decoder), control characters,
    /// non-ASCII characters (guards against Unicode look-alike and normalization tricks such as
    /// fullwidth dots), and any ".." that would climb above the root.
    /// Normalized: "." segments are dropped, repeated slashes collapse, ".." removes the previous segment.
    /// </summary>
    public static bool TryCanonicalize(string? path, out string canonical)
    {
        canonical = string.Empty;

        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            return false;
        }

        foreach (var ch in path)
        {
            // Printable ASCII only (space through '~'), excluding backslash and percent.
            if (ch < 0x20 || ch > 0x7E || ch == '\\' || ch == '%')
            {
                return false;
            }
        }

        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return false; // would escape above the root
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            sb.Append('/').Append(segment);
        }

        canonical = sb.Length == 0 ? "/" : sb.ToString();
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="targetPath"/> lies inside at least one allowed pattern.
    /// Pattern forms: "/dir/*" (strictly inside the directory "/dir") or "/dir/file" (exact match).
    /// A pattern that is malformed, or a target that cannot be canonicalized, never matches.
    /// Comparison is ordinal and case-sensitive, so a case variant is denied rather than granted.
    /// </summary>
    public static bool IsAllowed(string? targetPath, IEnumerable<string> allowedPatterns)
    {
        if (!TryCanonicalize(targetPath, out var target))
        {
            return false;
        }

        foreach (var pattern in allowedPatterns)
        {
            if (Matches(target, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(string canonicalTarget, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            if (!TryCanonicalize(pattern[..^1], out var baseDir))
            {
                return false;
            }

            var prefix = baseDir == "/" ? "/" : baseDir + "/";
            return canonicalTarget.StartsWith(prefix, StringComparison.Ordinal) && canonicalTarget.Length > prefix.Length;
        }

        if (pattern.Contains('*'))
        {
            return false; // unsupported wildcard form: fail closed
        }

        return TryCanonicalize(pattern, out var exact) && string.Equals(canonicalTarget, exact, StringComparison.Ordinal);
    }
}

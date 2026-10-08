using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0002: the G6 envelope check canonicalizes before it compares.</summary>
public class PathEnvelopeTests
{
    private static readonly string[] Ratified = ["/docs/ratified/*"];

    [Theory]
    [InlineData("/docs/ratified/a.md")]
    [InlineData("/docs/ratified//a.md")]
    [InlineData("/docs/ratified/./a.md")]
    [InlineData("/docs/ratified/sub/../a.md")]
    [InlineData("/docs/ratified/deep/er/a.md")]
    public void Allows_PathsInsideTheEnvelope(string path) =>
        Assert.True(PathEnvelope.IsAllowed(path, Ratified));

    [Theory]
    [InlineData("/docs/ratified/../x")]                 // the original bypass
    [InlineData("/docs/ratified/../../etc/passwd")]
    [InlineData("/docs/ratified/a/../../x")]
    [InlineData("/../docs/ratified/x")]                 // climbs above root
    [InlineData("/docs/ratified-evil/x")]               // sibling sharing a string prefix
    [InlineData("/docs/ratifiedx")]
    [InlineData("/docs/ratified")]                      // the directory itself is not "inside" it
    [InlineData("/docs/ratified/")]
    [InlineData("docs/ratified/x")]                     // relative
    [InlineData("/docs/ratified/%2e%2e/x")]             // percent-encoded dot segments
    [InlineData("/docs/ratified/..%2fx")]
    [InlineData("/docs/ratified/%2E%2E%5Cx")]
    [InlineData("/docs/ratified\\..\\x")]               // backslashes
    [InlineData("/docs\\ratified/x")]
    [InlineData("/docs/ratified/．．/x")]       // fullwidth dots
    [InlineData("/docs/ratified/café")]            // non-ASCII
    [InlineData("/DOCS/ratified/x")]                    // case variant is denied, not granted
    [InlineData("/docs/ratified/x\0")]                  // NUL
    [InlineData("/docs/ratified/x\n")]                  // control character
    [InlineData("")]
    [InlineData(null)]
    public void Denies_EverythingElse(string? path) =>
        Assert.False(PathEnvelope.IsAllowed(path, Ratified));

    [Theory]
    [InlineData("/a//b/./c/../d", "/a/b/d")]
    [InlineData("/", "/")]
    [InlineData("/a/..", "/")]
    [InlineData("///a///", "/a")]
    public void Canonicalizes(string input, string expected)
    {
        Assert.True(PathEnvelope.TryCanonicalize(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("/docs/ratified*")]       // unsupported wildcard form
    [InlineData("/docs/*/ratified/*")]
    [InlineData("")]
    public void MalformedPatterns_NeverMatch(string pattern) =>
        Assert.False(PathEnvelope.IsAllowed("/docs/ratified/x", [pattern]));

    [Fact]
    public void ExactPattern_MatchesOnlyThatFile()
    {
        Assert.True(PathEnvelope.IsAllowed("/ledger/./audit.log", ["/ledger/audit.log"]));
        Assert.False(PathEnvelope.IsAllowed("/ledger/audit.log2", ["/ledger/audit.log"]));
    }

    [Theory]
    [InlineData("Create", "/registry/business-cases/bc-1.json")]
    [InlineData("MarkApproved", "/docs/ratified/bc-1.md")]
    [InlineData("MarkApproved", "/ledger/audit/entry.json")]
    [InlineData("MarkRejected", "/docs/rejected/bc-1.md")]
    [InlineData("OverrideRitual", "/ledger/overrides/o-1.json")]
    public void ShippedRitualEnvelopes_StillAllowTheirOwnPaths(string ritual, string path) =>
        Assert.True(PathEnvelope.IsAllowed(path, RitualSeed.PathsFor(ritual)));

    [Fact]
    public void ShippedRitualEnvelopes_DoNotLeakAcrossRituals() =>
        Assert.False(PathEnvelope.IsAllowed("/docs/ratified/x", RitualSeed.PathsFor("MarkRejected")));
}

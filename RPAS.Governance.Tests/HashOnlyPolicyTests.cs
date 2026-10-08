using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0006: what a hash-only petitioner may send.</summary>
public class HashOnlyPolicyTests
{
    private static readonly string Sha256 = new('a', 64);

    private static string? Check(
        string? ritual = "EvidenceRecorded", string? entity = "doc-42", string? hash = null, string? algorithm = "SHA-256",
        string? keyId = null, Dictionary<string, object?>? metadata = null) =>
        HashOnlyPolicy.Validate(ritual, RitualSeed.EvidenceKeysFor(ritual), entity, hash ?? Sha256, algorithm, keyId, metadata);

    [Fact]
    public void AcceptsAWellFormedRecord() =>
        Assert.Null(Check(metadata: new() { ["documentId"] = "doc-42", ["documentVersion"] = 3, ["standardId"] = "ISO-27001" }));

    [Fact]
    public void AcceptsHmacWithKeyId() =>
        Assert.Null(Check(algorithm: "HMAC-SHA-256", keyId: "sidpa-key-2026-10"));

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsUnknownRituals(string? ritual) => Assert.NotNull(Check(ritual: ritual));

    [Theory]
    [InlineData("MD5")]
    [InlineData("sha-256")]
    [InlineData(null)]
    public void RejectsUnknownAlgorithms(string? algorithm) => Assert.NotNull(Check(algorithm: algorithm));

    [Theory]
    [InlineData("abc")]                           // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // uppercase
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // not hex
    public void RejectsMalformedHashes(string hash) => Assert.NotNull(Check(hash: hash));

    [Fact]
    public void Sha512HashRequires128HexCharacters()
    {
        Assert.NotNull(Check(algorithm: "SHA-512")); // 64 chars given
        Assert.Null(HashOnlyPolicy.Validate("EvidenceRecorded", RitualSeed.EvidenceKeysFor("EvidenceRecorded"), "d", new string('b', 128), "SHA-512", null, null));
    }

    [Fact]
    public void HmacRequiresAKeyId_AndPlainHashesRejectOne()
    {
        Assert.NotNull(Check(algorithm: "HMAC-SHA-256"));
        Assert.NotNull(Check(algorithm: "SHA-256", keyId: "k1"));
    }

    [Theory]
    [InlineData("Jane Doe")]                      // spaces: free text / names
    [InlineData("jane.doe@example.com")]          // e-mail
    [InlineData("a;DROP TABLE")]
    [InlineData("")]
    public void RejectsNonIdentifierValues(string value) =>
        Assert.NotNull(Check(metadata: new() { ["documentId"] = value }));

    [Fact]
    public void RejectsKeysThatAreNotAllowedForTheRitual() =>
        Assert.NotNull(Check(ritual: "IntentDeclared", metadata: new() { ["documentId"] = "d" }));

    [Fact]
    public void RejectsNestedOrNullValues()
    {
        Assert.NotNull(Check(metadata: new() { ["documentId"] = null }));
        Assert.NotNull(Check(metadata: new() { ["documentId"] = new object() }));
    }

    [Fact]
    public void RejectsNegativeNumbers_AndTooManyEntries()
    {
        Assert.NotNull(Check(metadata: new() { ["documentVersion"] = -1 }));

        var many = Enumerable.Range(0, HashOnlyPolicy.MaxMetadataEntries + 1).ToDictionary(i => $"k{i}", i => (object?)i);
        Assert.NotNull(Check(metadata: many));
    }

    [Fact]
    public void ErrorMessagesNeverEchoSubmittedValues()
    {
        const string secret = "Jane Doe SSN 123-45-6789";
        var message = Check(metadata: new() { ["documentId"] = secret });
        Assert.NotNull(message);
        Assert.DoesNotContain("Jane", message);
        Assert.DoesNotContain("123-45", message);

        var keyMessage = Check(metadata: new() { ["not allowed key with spaces"] = "v" });
        Assert.DoesNotContain("spaces", keyMessage);
    }
}

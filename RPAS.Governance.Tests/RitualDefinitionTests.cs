using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0007: the structure of a versioned ritual definition.</summary>
public class RitualDefinitionTests
{
    private static RitualDefinition Make(
        string ritual = "Heal", string? scope = RitualScopes.Heal, string[]? paths = null,
        bool evidence = false, string[]? keys = null, bool retired = false, string petitioner = "*", int version = 1) =>
        RitualDefinition.Create(petitioner, ritual, version, retired, evidence, keys, scope, paths ?? ["/governed/implementation/*"]);

    [Fact]
    public void AWellFormedTokenRitual_IsAccepted() => Assert.Null(Make().Validate());

    [Fact]
    public void EverySeededDefinition_IsValid()
    {
        foreach (var d in RitualSeed.Definitions)
        {
            Assert.Null(d.Validate());
        }
    }

    [Fact]
    public void Seed_CoversTheFourSidpaScopes_AndTheSixEvidenceRituals()
    {
        foreach (var scope in RitualScopes.All)
        {
            Assert.Single(RitualSeed.Definitions, d => d.Scope == scope);
        }
        Assert.Equal(6, RitualSeed.Definitions.Count(d => d.AcceptsEvidence));
        Assert.Equal(4, RitualSeed.Definitions.Count(d => d.Scope is null && !d.AcceptsEvidence)); // BusinessCase / ledger actions
    }

    [Theory]
    [InlineData("/governed/contracts/*")]
    [InlineData("/governed/contracts/suite-1/x")]
    [InlineData("/governed/intent/*")]
    [InlineData("/governed/./contracts/*")]         // not canonical: rejected rather than normalised
    public void HealScope_CanNeverBeGrantedContractOrIntentPaths(string path) =>
        Assert.NotNull(Make(scope: RitualScopes.Heal, paths: [path]).Validate());

    [Fact]
    public void ImplementScope_CannotBeGrantedContractPaths() =>
        Assert.NotNull(Make("Implement", RitualScopes.Implement, ["/governed/implementation/*", "/governed/contracts/*"]).Validate());

    [Fact]
    public void ARitualWithNoScope_CannotBeGrantedAReservedPath() =>
        Assert.NotNull(Make("Sneaky", scope: null, paths: ["/governed/contracts/*"]).Validate());

    [Fact]
    public void OnlyTheOwningScope_MayHoldAReservedArea()
    {
        Assert.Null(Make("EditContract", RitualScopes.EditContract, ["/governed/contracts/*"]).Validate());
        Assert.Null(Make("DeclareIntent", RitualScopes.DeclareIntent, ["/governed/intent/*"]).Validate());
    }

    [Fact]
    public void ReservedAreaMatch_IsOnSegmentBoundaries_NotRawPrefixes() =>
        Assert.Null(Make(scope: RitualScopes.Heal, paths: ["/governed/contracts-archive/*"]).Validate()); // a sibling, not the area

    [Theory]
    [InlineData("/governed/implementation/*", true)]
    [InlineData("governed/implementation/*", false)]   // relative
    [InlineData("/governed/**", false)]                // unsupported wildcard
    [InlineData("/gov*/x", false)]
    [InlineData("/governed/../contracts/*", false)]    // not canonical
    public void PathPatterns_MustBeCanonicalAndSupported(string path, bool ok) =>
        Assert.Equal(ok, Make(scope: RitualScopes.Implement, paths: [path]).Validate() is null);

    [Fact]
    public void ATokenScope_RequiresAtLeastOnePath() => Assert.NotNull(Make(paths: []).Validate());

    [Fact]
    public void UnknownScopes_AreRejected() => Assert.NotNull(Make(scope: "root").Validate());

    [Fact]
    public void MetadataKeys_AreOnlyValidForEvidenceRituals()
    {
        Assert.NotNull(Make(scope: null, paths: [], keys: ["moduleId"], evidence: false).Validate());
        Assert.Null(Make("Ev", scope: null, paths: [], keys: ["moduleId"], evidence: true).Validate());
    }

    [Fact]
    public void MetadataKeys_MustBeIdentifiers() =>
        Assert.NotNull(Make("Ev", scope: null, paths: [], keys: ["Jane Doe"], evidence: true).Validate());

    [Fact]
    public void ARetirement_CarriesNoGrants_AndIsValid() =>
        Assert.Null(Make(retired: true, scope: null, paths: []).Validate());

    [Fact]
    public void RitualAndPetitionerNames_MustBeIdentifiers()
    {
        Assert.NotNull(Make("Bad Name").Validate());
        Assert.NotNull(Make(petitioner: "bad petitioner").Validate());
        Assert.Null(Make(petitioner: "sidpa").Validate());
    }

    [Fact]
    public void Version_MustBePositive() => Assert.NotNull(Make(version: 0).Validate());

    [Fact]
    public void DefinitionHash_IsStable_AndIgnoresOrderOfKeysAndPaths()
    {
        var a = Make("Ev", scope: null, paths: ["/x/*", "/a/*"], keys: ["b", "a"], evidence: true);
        var b = Make("Ev", scope: null, paths: ["/a/*", "/x/*"], keys: ["a", "b"], evidence: true);
        Assert.Equal(a.DefinitionHash, b.DefinitionHash);
        Assert.Equal(64, a.DefinitionHash.Length);
    }

    [Fact]
    public void DefinitionHash_ChangesWithAnyGrant()
    {
        var baseline = Make().DefinitionHash;
        Assert.NotEqual(baseline, Make(paths: ["/governed/implementation/more/*"]).DefinitionHash);
        Assert.NotEqual(baseline, Make(version: 2).DefinitionHash);
        Assert.NotEqual(baseline, Make(retired: true).DefinitionHash);
        Assert.NotEqual(baseline, Make(petitioner: "sidpa").DefinitionHash);
    }

    [Fact]
    public void OnlyEditContract_IsHumanOnly()
    {
        Assert.True(RitualScopes.IsHumanOnly(RitualScopes.EditContract));
        Assert.False(RitualScopes.IsHumanOnly(RitualScopes.Heal));
        Assert.False(RitualScopes.IsHumanOnly(RitualScopes.Implement));
        Assert.False(RitualScopes.IsHumanOnly(RitualScopes.DeclareIntent));
        Assert.False(RitualScopes.IsHumanOnly(null));
    }
}

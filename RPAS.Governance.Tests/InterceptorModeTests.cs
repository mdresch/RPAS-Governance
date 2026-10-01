using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0004: the law-enforcement interceptor fails closed.</summary>
public class InterceptorModeTests
{
    private static RpasLawMode ModeFor(string? configured)
    {
        var values = new Dictionary<string, string?>();
        if (configured is not null)
        {
            values["Governance:RpasLawMode"] = configured;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new RpasLawEnforcementInterceptor(NullLogger<RpasLawEnforcementInterceptor>.Instance, config).Mode;
    }

    [Fact] public void Missing_DefaultsToEnforced() => Assert.Equal(RpasLawMode.Enforced, ModeFor(null));

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    public void Invalid_DefaultsToEnforced(string value) => Assert.Equal(RpasLawMode.Enforced, ModeFor(value));

    [Theory]
    [InlineData("Enforced", RpasLawMode.Enforced)]
    [InlineData("enforced", RpasLawMode.Enforced)]
    [InlineData("Advisory", RpasLawMode.Advisory)]
    public void ExplicitValues_AreHonoured(string value, RpasLawMode expected) => Assert.Equal(expected, ModeFor(value));
}

using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Api.Security;

/// <summary>
/// Which token scopes a petitioner may be issued, and who may change ritual definitions (AMD-2026-10-01-0007).
/// Both fail closed: a petitioner with no <c>Governance:Petitioners:{id}:Scopes</c> entry may be issued no scoped
/// token, and with no <c>Governance:Governors</c> entry nobody may publish a definition.
/// </summary>
public sealed class PetitionerAccess(IConfiguration configuration)
{
    /// <summary>Scopes configured for the petitioner (comma separated, or an array). Unknown names are ignored.</summary>
    public IReadOnlySet<string> ScopesFor(string petitionerId)
    {
        var section = configuration.GetSection($"Governance:Petitioners:{petitionerId}:Scopes");
        var values = new List<string>();
        if (section.Value is not null)
        {
            values.AddRange(section.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }
        values.AddRange(section.GetChildren().Select(c => c.Value).OfType<string>());

        return values.Where(RitualScopes.IsKnown).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>True when the named human (Entra object id) is listed in <c>Governance:Governors</c>.</summary>
    public bool IsGovernor(string humanId)
    {
        var section = configuration.GetSection("Governance:Governors");
        var listed = section.GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (section.Value is not null)
        {
            listed.AddRange(section.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }
        return listed.Contains(humanId, StringComparer.Ordinal);
    }
}

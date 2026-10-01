using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Api.Security;

/// <summary>
/// How a petitioner's content may be held (AMD-2026-10-01-0006). Configured per petitioner:
/// <c>Governance:Petitioners:{petitionerId}:ContentMode = FullContent</c>. Any petitioner that is not listed
/// is HashOnly (fail closed): the courthouse never receives document content from it.
/// </summary>
public sealed class PetitionerContentModes(IConfiguration configuration)
{
    public string Resolve(string petitionerId)
    {
        var configured = configuration[$"Governance:Petitioners:{petitionerId}:ContentMode"];
        return string.Equals(configured, "FullContent", StringComparison.OrdinalIgnoreCase)
            ? GovernanceLedgerEntry.ContentModeFull
            : GovernanceLedgerEntry.ContentModeHashOnly;
    }
}

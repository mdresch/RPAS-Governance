using System;
using RPAS.Governance.Core.Models.Exceptions;

namespace RPAS.Governance.Core.Models.Governance;

public class GovernanceLedgerEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    
    public string RitualType { get; init; } = "Phase0";
    
    public DateTimeOffset InitiatedAt { get; init; } = DateTimeOffset.UtcNow;
    
    public string Status { get; private set; } = "Completed";

    // Full-fidelity JSON storage for auditability
    public string? IdeationJson { get; private set; }
    
    public string? BusinessCaseJson { get; private set; }

    // Human oversight fields
    public string? GovernorNotes { get; private set; }
    
    public bool IsOverridden { get; private set; } = false;
    
    public string? OverrideJustification { get; private set; }

    // Parameterless constructor for EF Core
    protected GovernanceLedgerEntry() { }

    public GovernanceLedgerEntry(string ritualType, string? ideationJson = null, string? businessCaseJson = null)
    {
        RitualType = ritualType;
        IdeationJson = ideationJson;
        BusinessCaseJson = businessCaseJson;
    }

    public void AddGovernorNotes(string notes)
    {
        GovernorNotes = notes;
    }

    public void OverrideRitual(string justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new RpasLawViolationException("LedgerOverrideRule", "Cannot override a governance ledger without providing a justification.");
        }
        IsOverridden = true;
        OverrideJustification = justification;
        Status = "Overridden";
    }

    public void MarkInvalidated()
    {
        if (IsOverridden)
        {
            throw new RpasLawViolationException("LedgerStatusRule", "Cannot invalidate a ledger entry that was explicitly overridden by a Governor.");
        }
        Status = "Invalidated";
    }
}

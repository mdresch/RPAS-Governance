using System.Collections.Generic;

namespace RPAS.Governance.Core.Models.Governance;

public static class RitualEnvelope
{
    private static readonly Dictionary<string, List<string>> _envelopes = new()
    {
        { "Create", new List<string> { "/registry/business-cases/*" } },
        { "MarkApproved", new List<string> { "/docs/ratified/*", "/ledger/audit/*" } },
        { "MarkRejected", new List<string> { "/docs/rejected/*" } },
        { "OverrideRitual", new List<string> { "/ledger/overrides/*" } }
    };

    public static List<string> GetAllowedPaths(string ritualType)
    {
        return _envelopes.TryGetValue(ritualType, out var paths) ? paths : new List<string>();
    }
}

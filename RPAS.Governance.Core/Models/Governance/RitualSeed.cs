using System;
using System.Collections.Generic;
using System.Linq;

namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// The initial ritual definitions (AMD-2026-10-01-0007), version 1, for the default petitioner "*".
/// These are the former static envelope dictionary and hash-only allow-lists expressed as data. They are inserted
/// once, together with a ledger event, when the store is empty; every later change goes through the governed
/// publish operation. Nothing in the running system reads this class except the seeder.
/// </summary>
public static class RitualSeed
{
    public static IReadOnlyList<RitualDefinition> Definitions { get; } = Build();

    /// <summary>Paths a seeded ritual grants (empty when unknown). Convenience for tests and documentation.</summary>
    public static IReadOnlyList<string> PathsFor(string ritualType) =>
        Definitions.FirstOrDefault(d => d.RitualType == ritualType)?.AllowedPaths ?? [];

    /// <summary>Metadata keys a seeded evidence ritual allows, or null when the ritual is not an evidence ritual.</summary>
    public static IReadOnlyList<string>? EvidenceKeysFor(string? ritualType) =>
        Definitions.FirstOrDefault(d => d.RitualType == ritualType && d.AcceptsEvidence)?.MetadataKeys;

    private static List<RitualDefinition> Build()
    {
        const string star = RitualDefinition.DefaultPetitioner;

        RitualDefinition Action(string ritual, params string[] paths) =>
            RitualDefinition.Create(star, ritual, 1, false, false, null, null, paths);

        RitualDefinition Token(string ritual, string scope, string path) =>
            RitualDefinition.Create(star, ritual, 1, false, false, null, scope, [path]);

        RitualDefinition Evidence(string ritual, params string[] keys) =>
            RitualDefinition.Create(star, ritual, 1, false, true, keys, null, null);

        return
        [
            // Existing BusinessCase / ledger petitions (issued by /Validation/validate).
            Action("Create", "/registry/business-cases/*"),
            Action("MarkApproved", "/docs/ratified/*", "/ledger/audit/*"),
            Action("MarkRejected", "/docs/rejected/*"),
            Action("OverrideRitual", "/ledger/overrides/*"),

            // SIDPA scoped tokens (issued by /Tokens/issue). The paths are the agreed SIDPA layout and are
            // confirmed with SIDPA before go-live; the reserved areas are enforced by RitualScopes.
            Token("DeclareIntent", RitualScopes.DeclareIntent, "/governed/intent/*"),
            Token("Implement", RitualScopes.Implement, "/governed/implementation/*"),
            Token("Heal", RitualScopes.Heal, "/governed/implementation/*"),
            Token("EditContract", RitualScopes.EditContract, "/governed/contracts/*"),

            // SIDPA hash-only evidence (recorded by /Evidence/record).
            Evidence("IntentDeclared", "moduleId", "intentVersion", "standardId"),
            Evidence("ContractResult", "moduleId", "contractSuite", "contractVersion", "outcome", "attempt"),
            Evidence("HealAttempt", "moduleId", "attempt", "outcome", "scope"),
            Evidence("EvidenceRecorded", "documentId", "documentVersion", "standardId", "ruleSetId", "ruleSetVersion"),
            Evidence("ScoreAttested", "documentId", "documentVersion", "standardId", "ruleSetId", "ruleSetVersion", "scoreBand", "scoringMethodVersion"),
            Evidence("HumanAttestation", "documentId", "documentVersion", "reviewerRef", "decision", "scoreBand", "overrideApplied"),
        ];
    }
}

using System;
using RPAS.Governance.Core.Models.Exceptions;

namespace RPAS.Governance.Core.Models.Governance;

public class GovernanceLedgerEntry
{
    public const string ContentModeFull = "Full";
    public const string ContentModeHashOnly = "HashOnly";

    public Guid Id { get; init; } = Guid.NewGuid();

    public string RitualType { get; init; } = "Phase0";

    // Server-assigned (AMD-2026-10-01-0005): a petitioner-supplied timestamp is never trusted.
    public DateTimeOffset InitiatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public string Status { get; private set; } = "Completed";

    // Full-fidelity JSON storage for auditability
    public string? IdeationJson { get; private set; }

    public string? BusinessCaseJson { get; private set; }

    // Human oversight fields
    public string? GovernorNotes { get; private set; }

    public bool IsOverridden { get; private set; } = false;

    public string? OverrideJustification { get; private set; }

    // ---- Tamper evidence (AMD-2026-10-01-0005 / 0006) -------------------------------------------

    /// <summary>1-based position in the hash chain. Null on legacy rows written before the chain existed.</summary>
    public long? Sequence { get; private set; }

    /// <summary>EntryHash of the previous chained entry (64 zeros for the genesis entry).</summary>
    public string? PrevHash { get; private set; }

    /// <summary>SHA-256 over the canonical form of this entry including PrevHash. Null until sealed.</summary>
    public string? EntryHash { get; private set; }

    /// <summary>Authenticated petitioner that caused the entry.</summary>
    public string? PetitionerId { get; private set; }

    /// <summary>Set when this entry amends or annotates an earlier entry. Earlier entries are never modified.</summary>
    public Guid? RefersToEntryId { get; private set; }

    /// <summary>"Full" (content carried in the *Json fields) or "HashOnly" (only ProofJson: hash plus metadata).</summary>
    public string? ContentMode { get; private set; }

    /// <summary>Hash-only evidence payload: content hash, algorithm, key id and allow-listed metadata. Never document content.</summary>
    public string? ProofJson { get; private set; }

    /// <summary>True once the entry is part of the hash chain. Sealed entries are immutable.</summary>
    public bool IsSealed => EntryHash != null;

    // Parameterless constructor for EF Core
    protected GovernanceLedgerEntry() { }

    public GovernanceLedgerEntry(string ritualType, string? ideationJson = null, string? businessCaseJson = null)
    {
        RitualType = ritualType;
        IdeationJson = ideationJson;
        BusinessCaseJson = businessCaseJson;
    }

    /// <summary>Creates a hash-only entry: the courthouse keeps the proof, never the content.</summary>
    public static GovernanceLedgerEntry CreateHashOnly(string ritualType, string petitionerId, string proofJson, Guid? refersToEntryId = null)
    {
        return new GovernanceLedgerEntry(ritualType)
        {
            PetitionerId = petitionerId,
            ContentMode = ContentModeHashOnly,
            ProofJson = proofJson,
            RefersToEntryId = refersToEntryId
        };
    }

    /// <summary>Attributes a not-yet-sealed entry to a petitioner and records how its content is held.</summary>
    public GovernanceLedgerEntry Attribute(string petitionerId, string contentMode, Guid? refersToEntryId = null)
    {
        EnsureMutable("Attribute");
        PetitionerId = petitionerId;
        ContentMode = contentMode;
        RefersToEntryId = refersToEntryId;
        return this;
    }

    /// <summary>Adds the entry to the hash chain. Called only by the chain sealer, under its lock.</summary>
    public void Seal(long sequence, string prevHash, DateTimeOffset now)
    {
        EnsureMutable("Seal");

        // PostgreSQL stores microsecond precision; hash exactly what will be stored so verification is stable.
        var utc = now.UtcDateTime;
        InitiatedAt = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
        Sequence = sequence;
        PrevHash = prevHash;
        EntryHash = LedgerHasher.Compute(this);
    }

    public void AddGovernorNotes(string notes)
    {
        EnsureMutable(nameof(AddGovernorNotes));
        GovernorNotes = notes;
    }

    public void OverrideRitual(string justification)
    {
        EnsureMutable(nameof(OverrideRitual));
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
        EnsureMutable(nameof(MarkInvalidated));
        if (IsOverridden)
        {
            throw new RpasLawViolationException("LedgerStatusRule", "Cannot invalidate a ledger entry that was explicitly overridden by a Governor.");
        }
        Status = "Invalidated";
    }

    private void EnsureMutable(string operation)
    {
        if (IsSealed)
        {
            throw new RpasLawViolationException(
                "LedgerImmutability",
                $"Ledger entry {Id} is sealed in the hash chain; '{operation}' is not permitted. Record a new entry that refers to it instead.");
        }
    }
}

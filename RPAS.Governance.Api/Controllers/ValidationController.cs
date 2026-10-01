using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Api.Security;
using RPAS.Governance.Core.Models.Exceptions;
using RPAS.Governance.Core.Models.Rituals;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Controllers;

public class ValidationPetition
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}

[ApiController]
[Route("[controller]")]
public class ValidationController : ControllerBase
{
    private readonly GovernanceDbContext _db;
    private readonly ILogger<ValidationController> _logger;

    private readonly PetitionerContentModes _modes;

    public ValidationController(GovernanceDbContext db, ILogger<ValidationController> logger, PetitionerContentModes modes)
    {
        _db = db;
        _logger = logger;
        _modes = modes;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidationPetition petition)
    {
        // AMD-2026-10-01-0003: every petition is attributable to an authenticated petitioner, and the
        // token issued below is bound to that petitioner.
        var petitionerId = RpasAuthentication.GetPetitionerId(User);
        if (petitionerId is null)
        {
            return StatusCode(403, new { error = "Authenticated caller carries no petitioner identity." });
        }

        // AMD-2026-10-01-0006: a hash-only petitioner never sends content. It records evidence through /Evidence/record.
        if (_modes.Resolve(petitionerId) != GovernanceLedgerEntry.ContentModeFull)
        {
            return StatusCode(403, new { error = "This petitioner is hash-only: full-content petitions are not accepted. Use /Evidence/record." });
        }

        try
        {
            // AMD-2026-10-01-0005: once the hash chain exists, history is frozen. Changes to an existing ledger entry
            // are recorded as NEW entries that refer to it; the original row is never modified. The amendment still
            // carries forward the entry's current state and applies the same validated domain mutation, so a
            // justification is still required to override, an overridden entry still cannot be invalidated, and the
            // amendment's own Status/IsOverridden/GovernorNotes reflect the change, not just an audit note about it.
            var chainActive = await LedgerChain.IsActiveAsync(_db);
            GovernanceLedgerEntry? amendment = null;
            Guid? amendmentRefersTo = null;

            if (petition.EntityType == "BusinessCase")
            {
                if (petition.Action == "Create")
                {
                    var bc = petition.Payload.Deserialize<BusinessCase>();
                    if (bc == null) return BadRequest("Invalid BusinessCase payload.");
                    
                    _db.BusinessCases.Add(bc);
                }
                else if (petition.Action == "MarkApproved")
                {
                    var bc = await _db.BusinessCases.FirstOrDefaultAsync(x => x.Id == petition.EntityId);
                    if (bc == null) return NotFound($"BusinessCase {petition.EntityId} not found.");
                    
                    var justification = petition.Payload.TryGetProperty("justification", out var p) ? p.GetString() ?? "" : "";
                    bc.MarkApproved(justification);
                }
                else if (petition.Action == "MarkRejected")
                {
                    var bc = await _db.BusinessCases.FirstOrDefaultAsync(x => x.Id == petition.EntityId);
                    if (bc == null) return NotFound($"BusinessCase {petition.EntityId} not found.");
                    
                    var reason = petition.Payload.TryGetProperty("reason", out var p) ? p.GetString() ?? "" : "";
                    bc.MarkRejected(reason);
                }
                else
                {
                    return BadRequest($"Unsupported Action '{petition.Action}' for EntityType '{petition.EntityType}'.");
                }
            }
            else if (petition.EntityType == "GovernanceLedgerEntry")
            {
                // Compare GUIDs, not their text form (text casing differs between database providers).
                Guid.TryParse(petition.EntityId, out var ledgerEntryId);

                if (petition.Action == "Create")
                {
                    var entry = petition.Payload.Deserialize<GovernanceLedgerEntry>();
                    if (entry == null) return BadRequest("Invalid LedgerEntry payload.");
                    
                    entry.Attribute(petitionerId, GovernanceLedgerEntry.ContentModeFull);
                    _db.GovernanceLedgerEntries.Add(entry);
                }
                else if (petition.Action == "OverrideRitual")
                {
                    var entry = await _db.GovernanceLedgerEntries.FirstOrDefaultAsync(x => x.Id == ledgerEntryId);
                    if (entry == null) return NotFound($"LedgerEntry {petition.EntityId} not found.");
                    
                    var justification = petition.Payload.TryGetProperty("justification", out var p) ? p.GetString() ?? "" : "";
                    if (chainActive || entry.IsSealed)
                    {
                        amendment = GovernanceLedgerEntry.CreateAmendment(entry, petition.Action);
                        amendment.OverrideRitual(justification);
                        amendmentRefersTo = entry.Id;
                    }
                    else
                    {
                        entry.OverrideRitual(justification);
                    }
                }
                else if (petition.Action == "MarkInvalidated")
                {
                    var entry = await _db.GovernanceLedgerEntries.FirstOrDefaultAsync(x => x.Id == ledgerEntryId);
                    if (entry == null) return NotFound($"LedgerEntry {petition.EntityId} not found.");

                    if (chainActive || entry.IsSealed)
                    {
                        amendment = GovernanceLedgerEntry.CreateAmendment(entry, petition.Action);
                        amendment.MarkInvalidated();
                        amendmentRefersTo = entry.Id;
                    }
                    else
                    {
                        entry.MarkInvalidated();
                    }
                }
                else if (petition.Action == "AddGovernorNotes")
                {
                    var entry = await _db.GovernanceLedgerEntries.FirstOrDefaultAsync(x => x.Id == ledgerEntryId);
                    if (entry == null) return NotFound($"LedgerEntry {petition.EntityId} not found.");

                    var notes = petition.Payload.TryGetProperty("notes", out var p) ? p.GetString() ?? "" : "";
                    if (chainActive || entry.IsSealed)
                    {
                        amendment = GovernanceLedgerEntry.CreateAmendment(entry, petition.Action);
                        amendment.AddGovernorNotes(notes);
                        amendmentRefersTo = entry.Id;
                    }
                    else
                    {
                        entry.AddGovernorNotes(notes);
                    }
                }
                else
                {
                    return BadRequest($"Unsupported Action '{petition.Action}' for EntityType '{petition.EntityType}'.");
                }
            }
            else
            {
                return BadRequest($"Unsupported EntityType: {petition.EntityType}");
            }

            // 2. Audit Trail Allocation (Atomic with Mutation)
            // When the action amended a sealed/chained entry, the amendment itself IS the record: it already
            // carries the validated Status/IsOverridden/OverrideJustification/GovernorNotes, so it replaces the
            // generic audit stub rather than sitting alongside an empty one.
            GovernanceLedgerEntry ledgerEntry;
            if (amendment is not null)
            {
                ledgerEntry = amendment;
                ledgerEntry.Attribute(petitionerId, GovernanceLedgerEntry.ContentModeFull, amendmentRefersTo);
            }
            else
            {
                ledgerEntry = new GovernanceLedgerEntry(
                    petition.Action,
                    businessCaseJson: petition.Payload.ToString()
                );
                ledgerEntry.AddGovernorNotes($"Approved via Sovereign Extraction Verification Loop. Action: {petition.Action}. Petitioner: {petitionerId}");
                ledgerEntry.Attribute(petitionerId, GovernanceLedgerEntry.ContentModeFull);
            }
            _db.GovernanceLedgerEntries.Add(ledgerEntry);

            // 3. Issue Tokenized Authority (Phase-5 Step 5.2 & 5.3)
            // Authorized TTL = 120 seconds per Underwriter Ruling
            var authorityToken = new AuthorityToken(petition.Action, petition.EntityId, ttlSeconds: 120, petitionerId: petitionerId);
            
            // Step 5.3: Topology Binding (G6 Enforcement)
            var allowedPaths = RitualEnvelope.GetAllowedPaths(petition.Action);
            foreach (var path in allowedPaths)
            {
                authorityToken.AllowedPaths.Add(path);
            }

            _db.AuthorityTokens.Add(authorityToken);

            // The Underwriter explicitly mandated: "Validation is the mutation. DbContext.SaveChanges() is invoked."
            await LedgerChain.SaveChangesAsync(_db);

            return Ok(new 
            { 
                status = "valid", 
                message = "State transition approved and persisted in Sovereign Ledger.",
                authorityToken = new 
                {
                    id = authorityToken.Id,
                    expiresAt = authorityToken.ExpiresAt,
                    ritualType = authorityToken.RitualType
                }
            });
        }
        catch (LedgerContentionException)
        {
            Response.Headers.RetryAfter = "1";
            return StatusCode(503, new { error = "Ledger is busy; retry the request." });
        }
        catch (RpasLawViolationException ex)
        {
            // 409 Conflict for semantic/law conflicts - Logged for observability in Phase-4
            _logger.LogWarning("RPAS Law Violation: {RuleName} - {Message}", ex.RuleName, ex.Message);
            return Conflict(new { error = ex.Message, rule = ex.RuleName });
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while validating petition {Action} for {EntityType}.", petition.Action, petition.EntityType);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

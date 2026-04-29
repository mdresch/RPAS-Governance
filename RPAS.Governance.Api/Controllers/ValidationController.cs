using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

    public ValidationController(GovernanceDbContext db, ILogger<ValidationController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidationPetition petition)
    {
        try
        {
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
                if (petition.Action == "Create")
                {
                    var entry = petition.Payload.Deserialize<GovernanceLedgerEntry>();
                    if (entry == null) return BadRequest("Invalid LedgerEntry payload.");
                    
                    _db.GovernanceLedgerEntries.Add(entry);
                }
                else if (petition.Action == "OverrideRitual")
                {
                    var entry = await _db.GovernanceLedgerEntries.FirstOrDefaultAsync(x => x.Id.ToString() == petition.EntityId);
                    if (entry == null) return NotFound($"LedgerEntry {petition.EntityId} not found.");
                    
                    var justification = petition.Payload.TryGetProperty("justification", out var p) ? p.GetString() ?? "" : "";
                    entry.OverrideRitual(justification);
                }
                else if (petition.Action == "MarkInvalidated")
                {
                    var entry = await _db.GovernanceLedgerEntries.FirstOrDefaultAsync(x => x.Id.ToString() == petition.EntityId);
                    if (entry == null) return NotFound($"LedgerEntry {petition.EntityId} not found.");
                    
                    entry.MarkInvalidated();
                }
                else if (petition.Action == "AddGovernorNotes")
                {
                    var entry = await _db.GovernanceLedgerEntries.FirstOrDefaultAsync(x => x.Id.ToString() == petition.EntityId);
                    if (entry == null) return NotFound($"LedgerEntry {petition.EntityId} not found.");
                    
                    var notes = petition.Payload.TryGetProperty("notes", out var p) ? p.GetString() ?? "" : "";
                    entry.AddGovernorNotes(notes);
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
            var ledgerEntry = new GovernanceLedgerEntry(
                petition.Action,
                businessCaseJson: petition.Payload.ToString()
            );
            ledgerEntry.AddGovernorNotes($"Approved via Sovereign Extraction Verification Loop. Action: {petition.Action}");
            _db.GovernanceLedgerEntries.Add(ledgerEntry);

            // 3. Issue Tokenized Authority (Phase-5 Step 5.2 & 5.3)
            // Authorized TTL = 120 seconds per Underwriter Ruling
            var authorityToken = new AuthorityToken(petition.Action, petition.EntityId, ttlSeconds: 120);
            
            // Step 5.3: Topology Binding (G6 Enforcement)
            var allowedPaths = RitualEnvelope.GetAllowedPaths(petition.Action);
            foreach (var path in allowedPaths)
            {
                authorityToken.AllowedPaths.Add(path);
            }

            _db.AuthorityTokens.Add(authorityToken);

            // The Underwriter explicitly mandated: "Validation is the mutation. DbContext.SaveChanges() is invoked."
            await _db.SaveChangesAsync();

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
        catch (RpasLawViolationException ex)
        {
            // 409 Conflict for semantic/law conflicts - Logged for observability in Phase-4
            _logger.LogWarning("RPAS Law Violation: {RuleName} - {Message}", ex.RuleName, ex.Message);
            return Conflict(new { error = ex.Message, rule = ex.RuleName });
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

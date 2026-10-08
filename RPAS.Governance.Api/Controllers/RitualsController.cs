using Microsoft.AspNetCore.Mvc;
using RPAS.Governance.Api.Security;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Controllers;

public class RitualDefinitionRequest
{
    /// <summary>The petitioner the definition applies to, or "*" (the default when omitted) for all petitioners.</summary>
    public string? PetitionerId { get; set; }
    public string? RitualType { get; set; }
    public bool IsRetired { get; set; }
    public bool AcceptsEvidence { get; set; }
    public List<string>? MetadataKeys { get; set; }
    public string? Scope { get; set; }
    public List<string>? AllowedPaths { get; set; }
}

/// <summary>
/// Versioned ritual definitions (AMD-2026-10-01-0007). Reading shows what is in force for the calling petitioner, which
/// is what a client and the cross-repo contract tests assert against. Publishing a new version is limited to listed
/// governors acting as named humans, and every change is a ledger event written in the same save.
/// </summary>
[ApiController]
[Route("[controller]")]
public class RitualsController(GovernanceDbContext db, PetitionerAccess access) : ControllerBase
{
    [HttpGet("definitions")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var petitionerId = RpasAuthentication.GetPetitionerId(User);
        if (petitionerId is null)
        {
            return StatusCode(403, new { error = "Authenticated caller carries no petitioner identity." });
        }

        var definitions = await RitualDefinitionStore.ListInForceAsync(db, petitionerId, ct);
        return Ok(definitions.Select(Summary));
    }

    [HttpPost("definitions")]
    public async Task<IActionResult> Publish([FromBody] RitualDefinitionRequest request, CancellationToken ct)
    {
        var petitionerId = RpasAuthentication.GetPetitionerId(User);
        if (petitionerId is null)
        {
            return StatusCode(403, new { error = "Authenticated caller carries no petitioner identity." });
        }

        var humanId = RpasAuthentication.GetHumanId(User);
        if (humanId is null)
        {
            return StatusCode(403, new { error = "Ritual definitions are changed by a named human only; an automated petitioner cannot do it." });
        }
        if (!access.IsGovernor(humanId))
        {
            return StatusCode(403, new { error = "This user is not a governor." });
        }

        try
        {
            var published = await RitualDefinitionStore.PublishAsync(
                db,
                request.PetitionerId ?? RitualDefinition.DefaultPetitioner,
                request.RitualType ?? string.Empty,
                request.IsRetired,
                request.AcceptsEvidence,
                request.MetadataKeys,
                request.Scope,
                request.AllowedPaths,
                changedByHumanId: humanId,
                viaPetitionerId: petitionerId,
                ct);

            return Created($"/Rituals/definitions", Summary(published));
        }
        catch (RitualDefinitionException ex)
        {
            return ex.Message.StartsWith("Another publisher", StringComparison.Ordinal)
                ? Conflict(new { error = ex.Message })
                : BadRequest(new { error = ex.Message });
        }
        catch (LedgerContentionException)
        {
            Response.Headers.RetryAfter = "1";
            return StatusCode(503, new { error = "Ledger is busy; retry the request." });
        }
    }

    private static object Summary(RitualDefinition d) => new
    {
        petitionerId = d.PetitionerId,
        ritualType = d.RitualType,
        version = d.Version,
        acceptsEvidence = d.AcceptsEvidence,
        metadataKeys = d.MetadataKeys,
        scope = d.Scope,
        allowedPaths = d.AllowedPaths,
        definitionHash = d.DefinitionHash
    };
}

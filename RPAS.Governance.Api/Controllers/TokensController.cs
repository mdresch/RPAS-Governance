using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RPAS.Governance.Api.Security;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Controllers;

public class TokenIssueRequest
{
    public string? RitualType { get; set; }
    public string? EntityId { get; set; }
}

/// <summary>
/// Scoped authority tokens (AMD-2026-10-01-0007). This is how a hash-only petitioner such as SIDPA obtains a token:
/// no content is sent, only a ritual type and an entity identifier.
///
/// A token is issued only when (1) the ritual is defined and in force for the petitioner, (2) the petitioner has been
/// granted the ritual's scope, and (3) for a human-only scope (edit-contract) the request carries a delegated user
/// token naming a human. The issuance itself is a ledger entry written in the same save as the token.
/// </summary>
[ApiController]
[Route("[controller]")]
public class TokensController(
    GovernanceDbContext db, PetitionerAccess access, ILogger<TokensController> logger) : ControllerBase
{
    private const int TokenTtlSeconds = 120;

    [HttpPost("issue")]
    public async Task<IActionResult> Issue([FromBody] TokenIssueRequest request, CancellationToken ct)
    {
        var petitionerId = RpasAuthentication.GetPetitionerId(User);
        if (petitionerId is null)
        {
            return StatusCode(403, new { error = "Authenticated caller carries no petitioner identity." });
        }

        if (!HashOnlyPolicy.IsIdentifier(request.EntityId))
        {
            return BadRequest(new { error = "entityId must be an identifier (letters, digits and . _ : / + - only, 1-128 characters)." });
        }

        var definition = await RitualDefinitionStore.ResolveAsync(db, petitionerId, request.RitualType, ct);
        if (definition?.Scope is null)
        {
            return BadRequest(new { error = "ritualType is not a ritual that issues a scoped token." });
        }

        if (!access.ScopesFor(petitionerId).Contains(definition.Scope))
        {
            return StatusCode(403, new { error = $"This petitioner has not been granted scope '{definition.Scope}'." });
        }

        string? humanId = null;
        if (RitualScopes.IsHumanOnly(definition.Scope))
        {
            humanId = RpasAuthentication.GetHumanId(User);
            if (humanId is null)
            {
                return StatusCode(403, new { error = $"Scope '{definition.Scope}' is issued to a named human only; an automated petitioner cannot obtain it." });
            }
        }

        var token = new AuthorityToken(definition.RitualType, request.EntityId!, TokenTtlSeconds, petitionerId, definition.Scope, humanId);
        foreach (var path in definition.AllowedPaths)
        {
            token.AllowedPaths.Add(path);
        }

        var proof = JsonSerializer.Serialize(new
        {
            tokenId = token.Id,
            ritualType = definition.RitualType,
            scope = definition.Scope,
            entityId = request.EntityId,
            definitionVersion = definition.Version,
            definitionHash = definition.DefinitionHash,
            humanId
        });

        try
        {
            db.AuthorityTokens.Add(token);
            db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly("TokenIssued", petitionerId, proof));
            await LedgerChain.SaveChangesAsync(db, ct);
        }
        catch (LedgerContentionException)
        {
            Response.Headers.RetryAfter = "1";
            return StatusCode(503, new { error = "Ledger is busy; retry the request." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to issue a {Scope} token.", definition.Scope);
            return StatusCode(500, new { error = "An internal error occurred while issuing the token." });
        }

        return Created($"/Mutation/execute", new
        {
            id = token.Id,
            expiresAt = token.ExpiresAt,
            ritualType = token.RitualType,
            scope = token.Scope,
            allowedPaths = token.AllowedPaths
        });
    }
}

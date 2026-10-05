using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Persistence.Data;
using RPAS.Governance.Api.Security;
using RPAS.Governance.Core.Models.Governance;
using System;
using System.Threading.Tasks;

namespace RPAS.Governance.Api.Controllers;

public class MutationRequest
{
    public string TokenId { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
}

[ApiController]
[Route("[controller]")]
public class MutationController : ControllerBase
{
    private readonly GovernanceDbContext _db;

    public MutationController(GovernanceDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Simulates a G6-protected mutation that requires a valid AuthorityToken
    /// and enforces Topology (G6) at runtime.
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] MutationRequest request)
    {
        // 0. Identity (AMD-2026-10-01-0003): the caller must be an authenticated petitioner.
        var petitionerId = RpasAuthentication.GetPetitionerId(User);
        if (petitionerId is null)
        {
            return Forbidden(new { error = "Authenticated caller carries no petitioner identity." });
        }

        if (!Guid.TryParse(request.TokenId, out var id))
        {
            return BadRequest("Invalid Token ID format.");
        }

        // 1. Retrieval (read-only; the authoritative single-use decision is made atomically in step 5)
        var token = await _db.AuthorityTokens.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

        if (token == null)
        {
            return NotFound("Authority token not found.");
        }

        // 2. Binding: a token may only be used by the petitioner it was issued to. Tokens without a
        //    binding (issued before AMD-2026-10-01-0003) are never honoured.
        if (token.PetitionerId is null || !string.Equals(token.PetitionerId, petitionerId, StringComparison.Ordinal))
        {
            return Forbidden(new { error = "Authority token was not issued to this petitioner." });
        }

        // 3. Law Check: Expiry and Consumption (early, informative rejection)
        if (token.IsConsumed)
        {
            return Conflict(new { error = "Authority token has already been consumed (Replay detected)." });
        }

        if (DateTime.UtcNow > token.ExpiresAt)
        {
            return Conflict(new { error = "Authority token has expired (TTL Breach)." });
        }

        // 4. Topology Check (G6, AMD-2026-10-01-0002): canonicalize, then compare on segment boundaries.
        //    A path violation does not burn the token.
        if (!PathEnvelope.IsAllowed(request.TargetPath, token.AllowedPaths))
        {
            return Forbidden(new { 
                error = "Topology Violation (G6)", 
                path = request.TargetPath, 
                details = "Requested path is outside the authorized ritual envelope." 
            });
        }

        // 5. Atomic Consumption (AMD-2026-10-01-0001): one conditional UPDATE decides the winner.
        //    This MUST happen before any real mutation effect so a lost race performs no effect.
        var consumption = await AuthorityTokenStore.TryConsumeAsync(_db, id, DateTime.UtcNow);
        switch (consumption)
        {
            case TokenConsumeResult.Consumed:
                break;
            case TokenConsumeResult.NotFound:
                return NotFound("Authority token not found.");
            case TokenConsumeResult.AlreadyConsumed:
                return Conflict(new { error = "Authority token has already been consumed (Replay detected)." });
            default:
                return Conflict(new { error = "Authority token has expired (TTL Breach)." });
        }

        // 6. Mutation Effect (Simulated)
        // ... in a real G6 agent, this is where it would write to S3, FS, etc.

        return Ok(new 
        { 
            status = "executed", 
            message = "Mutation verified, Topology validated, and token consumed.",
            ritualType = token.RitualType,
            scope = token.Scope,
            entityId = token.EntityId,
            path = request.TargetPath
        });
    }

    private IActionResult Forbidden(object value) => StatusCode(403, value);
}

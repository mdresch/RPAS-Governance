using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Persistence.Data;
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
        if (!Guid.TryParse(request.TokenId, out var id))
        {
            return BadRequest("Invalid Token ID format.");
        }

        // 1. Atomic Verification & Retrieval
        var token = await _db.AuthorityTokens.FirstOrDefaultAsync(x => x.Id == id);
        
        if (token == null)
        {
            return NotFound("Authority token not found.");
        }

        // 2. Law Check: Expiry and Consumption
        if (token.IsConsumed)
        {
            return Conflict(new { error = "Authority token has already been consumed (Replay detected)." });
        }

        if (DateTime.UtcNow > token.ExpiresAt)
        {
            return Conflict(new { error = "Authority token has expired (TTL Breach)." });
        }

        // 3. Topology Check (Phase-5 Step 5.3 Mandate)
        // Simple prefix check for G6 simulation
        var isPathAllowed = token.AllowedPaths.Any(p => request.TargetPath.StartsWith(p.TrimEnd('*')));
        if (!isPathAllowed)
        {
            return Forbidden(new { 
                error = "Topology Violation (G6)", 
                path = request.TargetPath, 
                details = "Requested path is outside the authorized ritual envelope." 
            });
        }

        // 4. Mutation Effect (Simulated)
        // ... in a real G6 agent, this is where it would write to S3, FS, etc.
        
        // 5. Atomic Consumption (Phase-5 Step 5.2 Mandate)
        token.MarkConsumed();
        await _db.SaveChangesAsync();

        return Ok(new 
        { 
            status = "executed", 
            message = "Mutation verified, Topology validated, and token consumed.",
            ritualType = token.RitualType,
            entityId = token.EntityId,
            path = request.TargetPath
        });
    }

    private IActionResult Forbidden(object value) => StatusCode(403, value);
}

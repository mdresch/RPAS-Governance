using Microsoft.AspNetCore.Mvc;
using RPAS.Governance.Core.Models.Exceptions;

namespace RPAS.Governance.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class ReplayController : ControllerBase
{
    [HttpPost("replay")]
    public IActionResult Replay()
    {
        try
        {
            // Placeholder for replay
            return Ok(new { status = "replayed" });
        }
        catch (RpasLawViolationException ex)
        {
            return BadRequest(new { error = ex.Message, rule = ex.RuleName });
        }
        catch (System.Exception)
        {
            return StatusCode(500);
        }
    }
}

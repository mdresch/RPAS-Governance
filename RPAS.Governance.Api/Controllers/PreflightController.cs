using Microsoft.AspNetCore.Mvc;
using RPAS.Governance.Core.Models.Exceptions;

namespace RPAS.Governance.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class PreflightController : ControllerBase
{
    [HttpPost("check")]
    public IActionResult Check()
    {
        try
        {
            // Placeholder for preflight checks
            return Ok(new { status = "lawful" });
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

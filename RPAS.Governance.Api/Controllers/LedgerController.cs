using Microsoft.AspNetCore.Mvc;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Controllers;

/// <summary>Read-only integrity views over the ledger (AMD-2026-10-01-0005). Never returns entry content.</summary>
[ApiController]
[Route("[controller]")]
public class LedgerController(GovernanceDbContext db, IServiceProvider services) : ControllerBase
{
    [HttpGet("head")]
    public async Task<IActionResult> Head(CancellationToken ct)
    {
        var v = await LedgerVerifier.VerifyAsync(db, ct);
        return Ok(new { sequence = v.HeadSequence, entryHash = v.HeadHash, chainOk = v.Ok });
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var chain = await LedgerVerifier.VerifyAsync(db, ct);

        object? anchors = null;
        var anchorsOk = true;
        if (services.GetService<ILedgerAnchorSink>() is { } sink)
        {
            var result = await LedgerVerifier.VerifyAgainstAnchorsAsync(db, await sink.ReadAllAsync(ct), ct);
            anchorsOk = result.Ok;
            anchors = new { sink = sink.Name, result.Ok, result.AnchorsChecked, result.LatestAnchoredSequence, result.Problems };
        }

        var ok = chain.Ok && anchorsOk;
        return StatusCode(ok ? 200 : 409, new { ok, chain, anchors });
    }

    /// <summary>
    /// Replays the entire ledger event stream from sequence 1 to head, reconstituting state counts
    /// and verifying cryptographic integrity (AMD-2026-10-01-0011).
    /// </summary>
    [HttpGet("replay")]
    public async Task<IActionResult> Replay(CancellationToken ct)
    {
        var report = await LedgerVerifier.ReplayAsync(db, ct);
        return StatusCode(report.Ok ? 200 : 409, report);
    }
}

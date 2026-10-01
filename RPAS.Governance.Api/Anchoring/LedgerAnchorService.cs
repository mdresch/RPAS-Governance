using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Anchoring;

/// <summary>
/// Periodically commits the chain head to external storage (AMD-2026-10-01-0005). It refuses to anchor a chain that
/// fails verification, and refuses to add an anchor while an earlier anchor no longer matches the ledger, so a rewritten
/// history can never be "blessed" by a fresh anchor.
/// </summary>
public sealed class LedgerAnchorService(
    IServiceScopeFactory scopes,
    ILedgerAnchorSink sink,
    ILogger<LedgerAnchorService> logger,
    IConfiguration configuration) : BackgroundService
{
    public const string AnchorRitualType = "AnchorRecorded";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue("Governance:Anchoring:IntervalMinutes", 60);
        var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));

        // Let the application finish starting (migrations, first requests) before the first anchor.
        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("Governance:Anchoring:StartupDelaySeconds", 30)));
        try { await Task.Delay(startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await AnchorOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ledger anchoring failed; will retry at the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Anchors the current head. Returns the anchor, or null when there was nothing to do or it was unsafe to anchor.</summary>
    public async Task<LedgerAnchor?> AnchorOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();

        var verification = await LedgerVerifier.VerifyAsync(db, ct);
        if (!verification.Ok)
        {
            logger.LogCritical("Ledger chain verification FAILED at sequence {Sequence}: {Reason}. Not anchoring.", verification.FirstBrokenSequence, verification.Reason);
            return null;
        }
        if (verification.HeadSequence is null)
        {
            return null; // empty chain
        }

        var existing = await sink.ReadAllAsync(ct);
        var anchorCheck = await LedgerVerifier.VerifyAgainstAnchorsAsync(db, existing, ct);
        if (!anchorCheck.Ok)
        {
            logger.LogCritical("Existing anchors no longer match the ledger: {Problems}. Not anchoring.", string.Join(" | ", anchorCheck.Problems));
            return null;
        }

        var headRitual = await db.GovernanceLedgerEntries.AsNoTracking()
            .Where(e => e.Sequence == verification.HeadSequence)
            .Select(e => e.RitualType).FirstAsync(ct);
        if (headRitual == AnchorRitualType)
        {
            return null; // nothing new since the last anchor
        }

        var anchor = new LedgerAnchor(verification.HeadSequence.Value, verification.HeadHash!, DateTimeOffset.UtcNow, LedgerHasher.Version, sink.Name);
        await sink.WriteAsync(anchor, ct);

        var proof = JsonSerializer.Serialize(new { anchoredSequence = anchor.Sequence, anchoredHash = anchor.EntryHash, sink = sink.Name });
        db.GovernanceLedgerEntries.Add(GovernanceLedgerEntry.CreateHashOnly(AnchorRitualType, "rpas-system", proof));
        await LedgerChain.SaveChangesAsync(db, ct);

        logger.LogInformation("Anchored ledger head at sequence {Sequence} to {Sink}.", anchor.Sequence, sink.Name);
        return anchor;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RPAS.Governance.Core.Models.Exceptions;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Core.Models.Rituals;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace RPAS.Governance.Persistence.Data;

public class RpasLawEnforcementInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<RpasLawEnforcementInterceptor> _logger;
    private readonly RpasLawMode _operationMode;

    /// <summary>The effective enforcement mode.</summary>
    public RpasLawMode Mode => _operationMode;

    public RpasLawEnforcementInterceptor(ILogger<RpasLawEnforcementInterceptor> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        // Fail closed (AMD-2026-10-01-0004): Enforced is the default, for a missing or an invalid value.
        // Advisory must be selected explicitly via Governance:RpasLawMode.
        var configMode = configuration["Governance:RpasLawMode"] ?? "Enforced";
        if (Enum.TryParse<RpasLawMode>(configMode, true, out var parsedMode))
        {
            _operationMode = parsedMode;
        }
        else
        {
            _operationMode = RpasLawMode.Enforced;
            _logger.LogWarning("Invalid RpasLawMode '{Mode}'. Defaulting to Enforced.", configMode);
        }
        
        _logger.LogInformation("RPAS Law Enforcement Interceptor initialized in {Mode} mode.", _operationMode);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnforceLaws(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        EnforceLaws(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnforceLaws(DbContext? context)
    {
        if (context == null) return;

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Entity is GovernanceLedgerEntry ledgerEntry)
                {
                    ValidateLedgerEntry(ledgerEntry, entry.State);
                }
                else if (entry.Entity is BusinessCase businessCase)
                {
                    ValidateBusinessCase(businessCase, entry.State);
                }
            }
            catch (RpasLawViolationException ex)
            {
                if (_operationMode == RpasLawMode.Enforced)
                {
                    _logger.LogError(ex, "RPAS Law VIOLATED. Transaction voided. Rule: {RuleName}", ex.RuleName);
                    throw; // Hard abort the EF Core save.
                }
                else
                {
                    _logger.LogWarning(ex, "RPAS Law VIOLATED (Advisory). Allowing transaction. Rule: {RuleName}", ex.RuleName);
                }
            }
        }
    }

    private void ValidateLedgerEntry(GovernanceLedgerEntry entry, EntityState state)
    {
        if (entry.IsOverridden && string.IsNullOrWhiteSpace(entry.OverrideJustification))
        {
            throw new RpasLawViolationException(
                "LedgerOverrideConstraint", 
                "An overridden ledger entry must physically contain a justification.");
        }
    }

    private void ValidateBusinessCase(BusinessCase bc, EntityState state)
    {
        if (bc.ApprovalStatus == "APPROVED")
        {
            if (bc.Placeholders != null && bc.Placeholders.Any())
            {
                throw new RpasLawViolationException(
                    "BusinessCaseApprovalConstraint", 
                    "Business Case has placeholders but is marked APPROVED.");
            }
        }
    }
}

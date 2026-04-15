using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Client;
using RPAS.Governance.Client.DTOs;
using RPAS.Governance.Client.Exceptions;
using System;
using System.Threading.Tasks;

namespace SpokeService.Example;

public class AuditLoopSimulator
{
    private readonly IGovernanceClient _governance;

    public AuditLoopSimulator(IGovernanceClient governance)
    {
        _governance = governance;
    }

    public async Task ExecuteApprovalRitual()
    {
        string businessCaseId = "test-bc-ratified";
        string justification = "Verified via Petitioner SDK - CSR-42 Baseline.";

        try
        {
            Console.WriteLine($"[Petitioner] Requesting authority to approve {businessCaseId}...");
            
            // 1. Strongly-typed ritual call with mandatory justification
            var result = await _governance.ApproveBusinessCaseAsync(businessCaseId, justification);

            if (result.Success)
            {
                Console.WriteLine($"[Petitioner] Authority GRANTED.");
                Console.WriteLine($"[Petitioner] Token: {result.Token?.Id} (Expires: {result.Token?.ExpiresAt})");
                
                // 2. Explicit secondary step for mutation effect (as per Underwriter mandate)
                await ExecuteMutationEffect(result.Token?.Id, "/docs/ratified/manual_v1.pdf");
            }
        }
        catch (RpasLawViolationException ex)
        {
            // 3. Catch structural law violations specifically
            Console.WriteLine($"[Petitioner] BLOCK: Law Violation - {ex.RuleName}");
            Console.WriteLine($"[Petitioner] Details: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Petitioner] ERROR: {ex.Message}");
        }
    }

    private async Task ExecuteMutationEffect(Guid? tokenId, string path)
    {
        // This simulates a G6 agency (like a storage service) consuming the token
        Console.WriteLine($"[Petitioner] Executing mutation on path: {path} with token: {tokenId}");
        // In a real scenario, this would be an HTTP call to the MutationController
    }
}

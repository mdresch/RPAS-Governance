using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Adpa.Orchestrator.Models.Rituals;
using Adpa.Orchestrator.Controllers;
using System.Linq;

namespace Adpa.Orchestrator.Services;

public interface IRtmExecutionService
{
    Task<(bool Success, string Message)> ApplyAmendmentAsync(string amendmentId, string actor);
}

public class RtmExecutionService(ILogger<RtmExecutionService> logger) : IRtmExecutionService
{
    public async Task<(bool Success, string Message)> ApplyAmendmentAsync(string amendmentId, string actor)
    {
        var amendment = AdpaMemoryStore.RtmAmendments.FirstOrDefault(a => a.Id == amendmentId);
        if (amendment == null) return (false, "Amendment not found");

        var newRequirement = new RtmRequirement
        {
            BusinessCaseId = "MOCK",
            Description = amendment.ProposedDescription,
            Domain = "AMENDED",
            SourceVersion = amendment.SourceVersion + 1,
            Status = "ACTIVE"
        };
        
        AdpaMemoryStore.RtmRequirements.Add(newRequirement);
        return (true, $"Amendment applied successfully. New Requirement ID: {newRequirement.Id}");
    }
}

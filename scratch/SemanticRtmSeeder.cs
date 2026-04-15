using Adpa.Orchestrator.Models.Rituals;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using Adpa.Orchestrator.Controllers;

namespace Adpa.Orchestrator.Services;

public interface ISemanticRtmSeeder
{
    Task SeedFromBusinessCaseAsync(string businessCaseId, CancellationToken ct = default);
}

public class SemanticRtmSeeder(ILogger<SemanticRtmSeeder> logger) : ISemanticRtmSeeder
{
    public Task SeedFromBusinessCaseAsync(string businessCaseId, CancellationToken ct = default)
    {
        var reqDescription = "Auto Seeded Requirement from " + businessCaseId;
        var rtmEntry = new RtmRequirement
        {
            BusinessCaseId = businessCaseId,
            Description = reqDescription,
            Domain = "TECHNICAL",
            SourceVersion = 1,
            Status = "SEED_SUCCESS"
        };

        AdpaMemoryStore.RtmRequirements.Add(rtmEntry);
        logger.LogInformation("Successfully mock-processed RTM seeding");
        return Task.CompletedTask;
    }
}

using Microsoft.AspNetCore.Mvc;
using Adpa.Orchestrator.Clients;
using Adpa.Orchestrator.Models.Rituals;
using Adpa.Orchestrator.Services;
using Adpa.Orchestrator.Models.System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Adpa.Orchestrator.Controllers;

public record AmendmentProposalRequest(
    string TargetRequirementId, 
    string ProposedDescription, 
    string Justification,
    string Requester,
    string? AmendmentType = "REPLACEMENT",
    string? AmendmentSubType = "FULL_REPLACEMENT");

public record AmendmentDecisionRequest(
    string AmendmentId,
    string Status, // APPROVED or REJECTED
    string DecidedBy,
    string? DecisionNotes);

public record ApplyAmendmentRequest(
    string AmendmentId,
    string Actor);

public record MsrfValidationInput(
    string ProjectId,
    string Title,
    string Concept);

// Static in-memory cache for ADPA UI Drafts/Projections (No DB Contexts retained)
public static class AdpaMemoryStore
{
    public static List<IdeationSummary> IdeationSummaries { get; } = new();
    public static List<RtmRequirement> RtmRequirements { get; } = new();
    public static List<MsrfEvaluation> MsrfEvaluations { get; } = new();
    public static List<RtmAmendment> RtmAmendments { get; } = new();
}

[ApiController]
[Route("api/[controller]")]
public class RitualController(
    IntelligenceClient intelligence,
    GovernanceApiClient govClient,
    ILogger<RitualController> logger,
    ISemanticRtmSeeder rtmSeeder,
    IRtmExecutionService executionService,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    // ---------------------------------------------------------------------------
    // Phase 0: Ideation & Business Case
    // ---------------------------------------------------------------------------

    [HttpPost("phase0/ingest")]
    public async Task<ActionResult<IdeationSummary>> IngestIdeation([FromBody] IngestionRequest request)
    {
        try
        {
            logger.LogInformation("Starting Ideation Ingestion ritual for: {Filename}", request.Filename);
            
            var summary = await intelligence.IngestIdeationAsync(request.Filename, request.Content);
            
            if (summary == null)
            {
                return BadRequest("Failed to generate ideation summary from the provided input.");
            }

            // ADPA Cache projection
            AdpaMemoryStore.IdeationSummaries.Add(summary);

            return Ok(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: phase0/ingest");
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("msrf/validate")]
    public async Task<ActionResult<MsrfEvaluation>> ValidateMsrf([FromBody] MsrfValidationInput input)
    {
        try
        {
            logger.LogInformation("Starting MSRF Validation ritual for: {Title}", input.Title);
            
            var evaluation = await intelligence.ValidateMsrfAsync(input.ProjectId, input.Title, input.Concept);
            
            if (evaluation == null)
            {
                return BadRequest("Failed to perform MSRF validation ritual.");
            }

            AdpaMemoryStore.MsrfEvaluations.Add(evaluation);
            return Ok(evaluation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: msrf/validate");
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("phase0/business-case")]
    public async Task<ActionResult<object>> GenerateBusinessCase([FromBody] string ideationTitle)
    {
        try
        {
            logger.LogInformation("Starting Business Case generation ritual for summary: {Title}", ideationTitle);

            var summary = AdpaMemoryStore.IdeationSummaries.FirstOrDefault(s => s.Title == ideationTitle);
            if (summary == null)
            {
                return NotFound($"Ideation summary with title '{ideationTitle}' not found in the ADPA Cache.");
            }

            // Trigger Intelligence Ritual (this returns dynamic/anonymous object in ADPA since we deleted businesscase)
            // Wait, intelligence.GenerateBusinessCaseAsync previously returned strongly-typed BusinessCase.
            // Since we deleted BusinessCase, it must return a generic dynamic object or DTO.
            var businessCase = await intelligence.GenerateBusinessCaseAsync(summary);
            if (businessCase == null) return BadRequest("Failed to generate business case.");

            // Submit petition to Governance to persist truth
            await govClient.ValidateStateTransitionAsync(new ValidationPetition 
            { 
                EntityType = "BusinessCase", 
                Action = "Create", 
                Payload = businessCase 
            });

            return Ok(businessCase);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: phase0/business-case");
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("phase0/approve")]
    public async Task<IActionResult> ApproveBusinessCase([FromBody] string businessCaseId)
    {
        try
        {
            logger.LogInformation("Attempting to approve Business Case via Governance API: {BusinessCaseId}", businessCaseId);

            // Petition Governance to approve it. Governance fetches, validates, and persists it.
            await govClient.ValidateStateTransitionAsync(new ValidationPetition 
            { 
                EntityType = "BusinessCase", 
                EntityId = businessCaseId,
                Action = "MarkApproved",
                Payload = new { justification = "Approved via Application Orchestrator Proxy." }
            });

            // Trigger Semantic RTM Seeding (Post-Approval Ritual)
            logger.LogInformation("Business Case {BusinessCaseId} approved by Governance. Triggering Semantic RTM Seeder.", businessCaseId);
            await rtmSeeder.SeedFromBusinessCaseAsync(businessCaseId);

            return Ok(new { 
                Status = "APPROVED", 
                RtmSeeding = "COMPLETED", 
                BusinessCaseId = businessCaseId 
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: phase0/approve for ID {BusinessCaseId}", businessCaseId);
            return StatusCode(409, new { Error = "Approval ritual blocked by Governance Authority.", Detail = ex.Message });
        }
    }

    [HttpGet("ledger/ideation")]
    public ActionResult<List<IdeationSummary>> GetIdeationLedger()
    {
        return Ok(AdpaMemoryStore.IdeationSummaries);
    }

    [HttpGet("ledger/rtm")]
    public ActionResult<List<RtmRequirement>> GetRtmLedger()
    {
        return Ok(AdpaMemoryStore.RtmRequirements);
    }

    [HttpPost("rtm/research-advice/{targetId}")]
    public async Task<ActionResult<ResearchAdvice>> GetRtmResearchAdvice(string targetId)
    {
        try
        {
            logger.LogInformation("RPAS-CM AI Research ritual triggered for Requirement: {TargetId}", targetId);
            var ledger = AdpaMemoryStore.RtmRequirements;
            var advice = await intelligence.GetRtmResearchAdviceAsync(targetId, ledger);
            if (advice == null) return BadRequest("Failed to generate AI research advice.");
            return Ok(advice);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: rtm/research-advice");
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("rtm/propose-amendment")]
    public IActionResult ProposeRtmAmendment([FromBody] AmendmentProposalRequest request)
    {
        try
        {
            logger.LogInformation("Proposing RTM Amendment for Requirement: {TargetId}", request.TargetRequirementId);
            var target = AdpaMemoryStore.RtmRequirements.FirstOrDefault(r => r.Id == request.TargetRequirementId);
            
            if (target == null) return NotFound($"Target RTM Requirement {request.TargetRequirementId} not found.");

            var amendment = new RtmAmendment
            {
                TargetRequirementId = target.Id,
                OriginalDescription = target.Description,
                ProposedDescription = request.ProposedDescription,
                Justification = request.Justification,
                Requester = request.Requester ?? "SYSTEM",
                ApprovalStatus = "PENDING",
                AmendmentType = request.AmendmentType ?? "REPLACEMENT",
                AmendmentSubType = request.AmendmentSubType ?? "FULL_REPLACEMENT",
                SourceVersion = target.SourceVersion
            };

            AdpaMemoryStore.RtmAmendments.Add(amendment);
            return Ok(new { Status = "PROPOSED", AmendmentId = amendment.Id, TargetId = target.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: rtm/propose-amendment");
            return StatusCode(500, new { Error = "Amendment proposal failed.", Detail = ex.Message });
        }
    }

    [HttpPost("rtm/decide-amendment")]
    public IActionResult DecideRtmAmendment([FromBody] AmendmentDecisionRequest request)
    {
        try
        {
            var amendment = AdpaMemoryStore.RtmAmendments.FirstOrDefault(a => a.Id == request.AmendmentId);
            if (amendment == null) return NotFound($"RTM Amendment {request.AmendmentId} not found.");

            if (amendment.ApprovalStatus != "PENDING") return BadRequest($"Amendment {request.AmendmentId} has already been decided.");

            amendment.ApprovalStatus = request.Status.ToUpper();
            amendment.DecidedBy = request.DecidedBy;
            amendment.DecidedAt = DateTime.UtcNow;
            amendment.DecisionNotes = request.DecisionNotes;

            return Ok(new { Status = amendment.ApprovalStatus, AmendmentId = amendment.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: rtm/decide-amendment");
            return StatusCode(500, new { Error = "Amendment decision failed.", Detail = ex.Message });
        }
    }

    [HttpPost("rtm/apply-amendment")]
    public async Task<IActionResult> ApplyRtmAmendment([FromBody] ApplyAmendmentRequest request)
    {
        try
        {
            var result = await executionService.ApplyAmendmentAsync(request.AmendmentId, request.Actor);
            if (!result.Success) return BadRequest(result.Message);
            return Ok(new { Message = result.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ritual Failure: rtm/apply-amendment");
            return StatusCode(500, new { Error = "Amendment application failed.", Detail = ex.Message });
        }
    }

    [HttpGet("system/health")]
    public async Task<ActionResult<SystemHealthResult>> GetSystemHealth()
    {
        try
        {
            bool intelligenceHealthy = await intelligence.CheckHealthAsync();
            var activeRituals = AdpaMemoryStore.IdeationSummaries.Count;

            var result = new SystemHealthResult(
                DbHealthy: true, // No DB in ADPA
                MessagingHealthy: true,
                IntelligenceHealthy: intelligenceHealthy,
                ActiveRituals: activeRituals,
                EnvironmentBaseline: "CSR-42-ADPA-ORCHESTRATOR-SOVEREIGN",
                Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64)
            );

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Integrity Failure: system/health");
            return StatusCode(500, ex.Message);
        }
    }
}

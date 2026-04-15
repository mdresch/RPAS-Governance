using System;
using System.Collections.Generic;

namespace RPAS.Governance.Client.DTOs;

public record BusinessCaseApproveRequest(string Justification);

public record BusinessCaseRejectRequest(string Reason);

public record BusinessCaseCreateRequest(
    string Id,
    string ExecutiveSummary,
    string ProblemStatement,
    string ProposedSolution,
    string Recommendation,
    string IdeationSummaryId,
    List<string> ExpectedBenefits,
    List<CostItemDto> EstimatedCosts,
    List<string> KeyRisks,
    List<string> CoreRequirements,
    List<string> Placeholders,
    int Version = 1
);

public record CostItemDto(string Description, decimal Amount, string Category);

/// <summary>
/// Internal DTO representing the raw petition format expected by the Governance API.
/// </summary>
internal record ValidationPetitionDto(
    string EntityType,
    string EntityId,
    string Action,
    object Payload
);

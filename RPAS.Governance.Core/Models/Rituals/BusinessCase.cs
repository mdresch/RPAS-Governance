using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using RPAS.Governance.Core.Models.Exceptions;

namespace RPAS.Governance.Core.Models.Rituals;

public class CostItem
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public double? Amount { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "USD";

    [JsonPropertyName("is_placeholder")]
    public bool IsPlaceholder { get; init; }
}

public class BusinessCase
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("ideation_summary_id")]
    public string IdeationSummaryId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; private set; } = 1;

    [JsonPropertyName("executive_summary")]
    public string ExecutiveSummary { get; private set; } = string.Empty;

    [JsonPropertyName("problem_statement")]
    public string ProblemStatement { get; private set; } = string.Empty;

    [JsonPropertyName("proposed_solution")]
    public string ProposedSolution { get; private set; } = string.Empty;

    [JsonPropertyName("expected_benefits")]
    public List<string> ExpectedBenefits { get; private set; } = new();

    [JsonPropertyName("estimated_costs")]
    public List<CostItem> EstimatedCosts { get; private set; } = new();

    [JsonPropertyName("key_risks")]
    public List<string> KeyRisks { get; private set; } = new();

    [JsonPropertyName("core_requirements")]
    public List<string> CoreRequirements { get; private set; } = new();

    [JsonPropertyName("recommendation")]
    public string Recommendation { get; private set; } = string.Empty;

    [JsonPropertyName("placeholders")]
    public List<string> Placeholders { get; private set; } = new();

    [JsonPropertyName("approval_status")]
    public string ApprovalStatus { get; private set; } = "PENDING";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    protected BusinessCase() { }

    public BusinessCase(string id, string ideationSummaryId)
    {
        Id = id;
        IdeationSummaryId = ideationSummaryId;
    }

    public void MarkApproved(string justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new RpasLawViolationException("BusinessCaseApproveRule", "A Business Case cannot be approved without a justification summary.");
        }
        
        if (Placeholders.Any())
        {
            throw new RpasLawViolationException("BusinessCaseCompletenessRule", "Cannot approve a Business Case that still contains placeholders.");
        }

        ApprovalStatus = "APPROVED";
    }

    public void MarkRejected(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new RpasLawViolationException("BusinessCaseRejectRule", "A rejection requires a formal reason.");
        }

        ApprovalStatus = "REJECTED";
    }
}

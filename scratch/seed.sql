INSERT INTO "BusinessCases" (
    "Id", "ApprovalStatus", "Version", "CreatedAt", 
    "ExpectedBenefits", "EstimatedCosts", "KeyRisks", "CoreRequirements", "Placeholders",
    "ExecutiveSummary", "ProblemStatement", "ProposedSolution", "Recommendation", "IdeationSummaryId"
) VALUES (
    'test-bc-ratified', 'PENDING', 1, now(), 
    '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb,
    '', '', '', '', ''
);

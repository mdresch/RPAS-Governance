using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using RPAS.Governance.Core.Models.Rituals;

#nullable disable

namespace RPAS.Governance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessCases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IdeationSummaryId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "text", nullable: false),
                    ProblemStatement = table.Column<string>(type: "text", nullable: false),
                    ProposedSolution = table.Column<string>(type: "text", nullable: false),
                    ExpectedBenefits = table.Column<string>(type: "jsonb", nullable: false),
                    EstimatedCosts = table.Column<List<CostItem>>(type: "jsonb", nullable: false),
                    KeyRisks = table.Column<string>(type: "jsonb", nullable: false),
                    CoreRequirements = table.Column<string>(type: "jsonb", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    Placeholders = table.Column<string>(type: "jsonb", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessCases", x => x.Id);
                    table.CheckConstraint("CK_BusinessCase_Status", "\"ApprovalStatus\" IN ('PENDING', 'APPROVED', 'REJECTED')");
                });

            migrationBuilder.CreateTable(
                name: "governance_ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RitualType = table.Column<string>(type: "text", nullable: false),
                    InitiatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IdeationJson = table.Column<string>(type: "text", nullable: true),
                    BusinessCaseJson = table.Column<string>(type: "text", nullable: true),
                    GovernorNotes = table.Column<string>(type: "text", nullable: true),
                    IsOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideJustification = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_governance_ledger", x => x.Id);
                    table.CheckConstraint("CK_Ledger_Override", "(\"IsOverridden\" = false) OR (\"IsOverridden\" = true AND \"OverrideJustification\" IS NOT NULL AND \"OverrideJustification\" != '')");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessCases");

            migrationBuilder.DropTable(
                name: "governance_ledger");
        }
    }
}

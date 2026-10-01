using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RPAS.Governance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerHashChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentMode",
                table: "governance_ledger",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryHash",
                table: "governance_ledger",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PetitionerId",
                table: "governance_ledger",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevHash",
                table: "governance_ledger",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofJson",
                table: "governance_ledger",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefersToEntryId",
                table: "governance_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "governance_ledger",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_governance_ledger_Sequence",
                table: "governance_ledger",
                column: "Sequence",
                unique: true);

            // AMD-2026-10-01-0005: database-level append-only enforcement (PostgreSQL). This protects the ledger even
            // from a role that holds UPDATE/DELETE privileges, and from direct SQL that bypasses the application.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION governance_ledger_guard() RETURNS trigger AS $guard$
BEGIN
    IF TG_OP = 'TRUNCATE' THEN
        RAISE EXCEPTION 'RPAS-LEDGER: governance_ledger is append-only; TRUNCATE is not permitted'
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'RPAS-LEDGER: governance_ledger is append-only; rows are never deleted'
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;

    -- UPDATE: only unchained legacy rows, and only until the chain exists (genesis entry = Sequence 1).
    IF OLD.""EntryHash"" IS NOT NULL OR EXISTS (SELECT 1 FROM governance_ledger WHERE ""Sequence"" = 1) THEN
        RAISE EXCEPTION 'RPAS-LEDGER: governance_ledger is append-only; row % is immutable', OLD.""Id""
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;

    RETURN NEW;
END;
$guard$ LANGUAGE plpgsql;");

                migrationBuilder.Sql(@"
CREATE TRIGGER governance_ledger_no_update_delete
    BEFORE UPDATE OR DELETE ON governance_ledger
    FOR EACH ROW EXECUTE FUNCTION governance_ledger_guard();");

                migrationBuilder.Sql(@"
CREATE TRIGGER governance_ledger_no_truncate
    BEFORE TRUNCATE ON governance_ledger
    FOR EACH STATEMENT EXECUTE FUNCTION governance_ledger_guard();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("DROP TRIGGER IF EXISTS governance_ledger_no_truncate ON governance_ledger;");
                migrationBuilder.Sql("DROP TRIGGER IF EXISTS governance_ledger_no_update_delete ON governance_ledger;");
                migrationBuilder.Sql("DROP FUNCTION IF EXISTS governance_ledger_guard();");
            }

            migrationBuilder.DropIndex(
                name: "IX_governance_ledger_Sequence",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "ContentMode",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "EntryHash",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "PetitionerId",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "PrevHash",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "ProofJson",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "RefersToEntryId",
                table: "governance_ledger");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "governance_ledger");
        }
    }
}

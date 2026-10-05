using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RPAS.Governance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRitualDefinitionsAndTokenScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HumanId",
                table: "authority_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "authority_tokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ritual_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PetitionerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RitualType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptsEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    MetadataKeys = table.Column<string>(type: "jsonb", nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AllowedPaths = table.Column<string>(type: "jsonb", nullable: false),
                    DefinitionHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ritual_definitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ritual_definitions_PetitionerId_RitualType_Version",
                table: "ritual_definitions",
                columns: new[] { "PetitionerId", "RitualType", "Version" },
                unique: true);

            // AMD-2026-10-01-0007: a ritual definition version is immutable. A change is a new version row, so UPDATE,
            // DELETE and TRUNCATE are refused at the database, even for a role that holds those privileges.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION ritual_definitions_guard() RETURNS trigger AS $guard$
BEGIN
    RAISE EXCEPTION 'RPAS-RITUALS: ritual_definitions is append-only; publish a new version instead (% not permitted)', TG_OP
        USING ERRCODE = 'integrity_constraint_violation';
END;
$guard$ LANGUAGE plpgsql;");

                migrationBuilder.Sql(@"
CREATE TRIGGER ritual_definitions_no_update_delete
    BEFORE UPDATE OR DELETE ON ritual_definitions
    FOR EACH ROW EXECUTE FUNCTION ritual_definitions_guard();");

                migrationBuilder.Sql(@"
CREATE TRIGGER ritual_definitions_no_truncate
    BEFORE TRUNCATE ON ritual_definitions
    FOR EACH STATEMENT EXECUTE FUNCTION ritual_definitions_guard();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("DROP TRIGGER IF EXISTS ritual_definitions_no_truncate ON ritual_definitions;");
                migrationBuilder.Sql("DROP TRIGGER IF EXISTS ritual_definitions_no_update_delete ON ritual_definitions;");
                migrationBuilder.Sql("DROP FUNCTION IF EXISTS ritual_definitions_guard();");
            }

            migrationBuilder.DropTable(
                name: "ritual_definitions");

            migrationBuilder.DropColumn(
                name: "HumanId",
                table: "authority_tokens");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "authority_tokens");
        }
    }
}

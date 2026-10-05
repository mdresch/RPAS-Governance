using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Rituals;
using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Persistence.Data;

public class GovernanceDbContext(DbContextOptions<GovernanceDbContext> options) : DbContext(options)
{
    public DbSet<BusinessCase> BusinessCases => Set<BusinessCase>();
    public DbSet<GovernanceLedgerEntry> GovernanceLedgerEntries => Set<GovernanceLedgerEntry>();
    public DbSet<AuthorityToken> AuthorityTokens => Set<AuthorityToken>();
    public DbSet<RitualDefinition> RitualDefinitions => Set<RitualDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ... existing mappings ...

        // Map the AuthorityToken
        modelBuilder.Entity<AuthorityToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AllowedPaths)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");
            entity.Property(e => e.Scope).HasMaxLength(32);
            entity.Property(e => e.HumanId).HasMaxLength(128);
            
            entity.ToTable("authority_tokens");
        });

        // Map the RitualDefinition (AMD-2026-10-01-0007). Versions are immutable; one row per (petitioner, ritual, version).
        modelBuilder.Entity<RitualDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PetitionerId, e.RitualType, e.Version }).IsUnique();
            entity.Property(e => e.PetitionerId).HasMaxLength(128);
            entity.Property(e => e.RitualType).HasMaxLength(128);
            entity.Property(e => e.Scope).HasMaxLength(32);
            entity.Property(e => e.DefinitionHash).HasMaxLength(64);
            entity.Property(e => e.MetadataKeys)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");
            entity.Property(e => e.AllowedPaths)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");
            entity.ToTable("ritual_definitions");
        });

        // Map the BusinessCase
        modelBuilder.Entity<BusinessCase>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Map complex lists as JSON strings for Postgres jsonb support via ValueConverters
            entity.Property(e => e.ExpectedBenefits)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");

            entity.Property(e => e.EstimatedCosts)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<CostItem>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<CostItem>())
                  .HasColumnType("jsonb");

            entity.Property(e => e.KeyRisks)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");

            entity.Property(e => e.Placeholders)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");

            entity.Property(e => e.CoreRequirements)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>())
                  .HasColumnType("jsonb");

            entity.ToTable("BusinessCases", t => t.HasCheckConstraint("CK_BusinessCase_Status", "\"ApprovalStatus\" IN ('PENDING', 'APPROVED', 'REJECTED')"));
        });

        // Map the GovernanceLedgerEntry
        modelBuilder.Entity<GovernanceLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            // A chain position can be used once: this is what makes forking the chain impossible (AMD-2026-10-01-0005).
            entity.HasIndex(e => e.Sequence).IsUnique();
            entity.ToTable("governance_ledger", t => t.HasCheckConstraint("CK_Ledger_Override", "(\"IsOverridden\" = false) OR (\"IsOverridden\" = true AND \"OverrideJustification\" IS NOT NULL AND \"OverrideJustification\" != '')"));
        });
    }
}

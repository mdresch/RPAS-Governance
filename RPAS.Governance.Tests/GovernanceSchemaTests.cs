using System.Text.Json;
using System.Text.RegularExpressions;

namespace RPAS.Governance.Tests;

public class GovernanceSchemaTests
{
    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "RPAS.Governance.slnx")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        throw new InvalidOperationException("Could not find repository root containing RPAS.Governance.slnx.");
    }

    [Fact]
    public void Guardrails_ContainAllCanonicalG1ToG6Definitions()
    {
        var repoRoot = FindRepoRoot();
        var guardrailsPath = Path.Combine(repoRoot, "governance-docs", "rpas-guardrails.json");
        Assert.True(File.Exists(guardrailsPath), $"File not found: {guardrailsPath}");

        var json = File.ReadAllText(guardrailsPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("RPAS-CM-GRA-001", root.GetProperty("artifact_id").GetString());
        Assert.Equal("CSR-42", root.GetProperty("csr_epoch").GetString());

        var guardrails = root.GetProperty("guardrails");

        // Canonical G1 to G6 must all exist and have non-empty definitions
        Assert.True(guardrails.TryGetProperty("G1_AUTHORITY_BOUNDARY", out var g1) && !string.IsNullOrWhiteSpace(g1.GetString()));
        Assert.True(guardrails.TryGetProperty("G2_LIFECYCLE_INTEGRITY", out var g2) && !string.IsNullOrWhiteSpace(g2.GetString()));
        Assert.True(guardrails.TryGetProperty("G3_EVIDENCE_LINEAGE", out var g3) && !string.IsNullOrWhiteSpace(g3.GetString()));
        Assert.True(guardrails.TryGetProperty("G4_DETERMINISM", out var g4) && !string.IsNullOrWhiteSpace(g4.GetString()));
        Assert.True(guardrails.TryGetProperty("G5_READ_VS_ACT", out var g5) && !string.IsNullOrWhiteSpace(g5.GetString()));
        Assert.True(guardrails.TryGetProperty("G6_TOPOLOGY_BOUNDARY", out var g6) && !string.IsNullOrWhiteSpace(g6.GetString()));

        // Contradictory pre-extraction names must be absent
        Assert.False(guardrails.TryGetProperty("G2_TASK_TAXONOMY", out _), "G2_TASK_TAXONOMY should be G2_LIFECYCLE_INTEGRITY");
        Assert.False(guardrails.TryGetProperty("G4_COLLISION_PREVENTION", out _), "G4_COLLISION_PREVENTION should be G4_DETERMINISM");

        // Gates check: Gate 2 must reference RPAS.Governance.slnx
        var gates = root.GetProperty("gates");
        Assert.Equal(5, gates.GetArrayLength());

        var gate2 = gates[1];
        Assert.Equal(2, gate2.GetProperty("id").GetInt32());
        var gate2Requirements = gate2.GetProperty("requirements");
        var hasSlnx = false;
        foreach (var req in gate2Requirements.EnumerateArray())
        {
            if (req.GetString()?.Contains("RPAS.Governance.slnx") == true)
            {
                hasSlnx = true;
                break;
            }
        }
        Assert.True(hasSlnx, "Gate 2 must reference RPAS.Governance.slnx");
    }

    [Fact]
    public void Attestation_ConformsToSchemaAndAllDeclaredScopeFilesExist()
    {
        var repoRoot = FindRepoRoot();
        var attestationPath = Path.Combine(repoRoot, "governance-docs", "rpas-attestation.json");
        Assert.True(File.Exists(attestationPath), $"File not found: {attestationPath}");

        var json = File.ReadAllText(attestationPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Traceability
        var traceability = root.GetProperty("traceability");
        var artifactId = traceability.GetProperty("artifactId").GetString();
        Assert.NotNull(artifactId);
        Assert.Matches(new Regex(@"^RPAS-CM-[A-Z]+-[0-9]{3}$"), artifactId);

        var origin = traceability.GetProperty("origin").GetString();
        Assert.Contains(origin, new[] { "Intelligence", "Experience", "Orchestration", "Human" });

        var csrVersion = traceability.GetProperty("csrVersion").GetString();
        Assert.NotNull(csrVersion);
        Assert.Matches(new Regex(@"^v[0-9]+(\.[0-9]+)*\+CSR\.[A-Z0-9-:]+$"), csrVersion);

        // Authority
        var authority = root.GetProperty("authority");
        var tier = authority.GetProperty("tier").GetString();
        Assert.Contains(tier, new[] { "Intelligence", "Experience", "Orchestration", "Data" });

        var ritualRole = authority.GetProperty("ritualRole").GetString();
        Assert.Contains(ritualRole, new[] { "Proposer", "Approver", "Executor" });

        // Collision Prevention Scope
        var collision = root.GetProperty("collision_prevention");
        var scope = collision.GetProperty("scope");
        Assert.True(scope.GetArrayLength() > 0, "Scope must have at least one declared item.");

        foreach (var item in scope.EnumerateArray())
        {
            var relativePath = item.GetString();
            Assert.NotNull(relativePath);
            var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(
                File.Exists(fullPath) || Directory.Exists(fullPath),
                $"Declared scope item does not exist in repository: '{relativePath}' (full path: {fullPath})");
        }
    }
}

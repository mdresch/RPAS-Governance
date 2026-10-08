# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0008 — Schema Reconciliation & Manifest Hygiene

## 1. Metadata
- **Domain**: GOV (Governance process / tooling) — see RPAS‑CM‑NAM‑001 §3.2
- **Change Type**: FIX (Hotfix) + REP (Replacement) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive/corrective; aligns JSON manifests with canonical G1–G6 invariants and CSR‑42 baseline)
- **Basis / CSR Target**: CSR‑42
- **Reference**: RPAS‑CM Fit‑Gap Analysis (AMD‑2026‑10‑01‑0012 follow‑up on stale pre‑extraction artifacts)
- **Task Class**: TCL-CFG + TCL-GOV
- **Depends on**: AMD‑2026‑10‑01‑0012 (CI/CD Gate Automation), RPAS‑CM‑NAM‑001 (Governance Registry)

## 2. Change Description

**Problem.** During the implementation of AMD‑2026‑10‑01‑0012, two governance manifests in `governance-docs/` were identified as stale remnants from the pre‑extraction ADPA monorepo (`a22576b`):
1. `governance-docs/rpas-guardrails.json` contained severe discrepancies with the authoritative `RPAS.md` (`RPAS‑CM‑GRA‑001` v2.3.0):
   - Defined `G2_TASK_TAXONOMY` and `G4_COLLISION_PREVENTION`, directly contradicting `RPAS.md`'s canonical G2 (`Lifecycle Integrity`) and G4 (`Determinism`).
   - Omitted `G6_TOPOLOGY_BOUNDARY` (formally registered in `RPAS‑CM‑NAM‑001`).
   - Referenced non‑existent legacy components: `.NET Orchestration solution build (Adpa.sln)`, `Next.js Experience Tier build (pnpm build)`, and `Adpa.Orchestrator`.
2. `governance-docs/rpas-attestation.json` referenced 11 files in its `collision_prevention.scope` (`orchestrator/Adpa.AppHost/Program.cs`, `orchestrator/Adpa.Orchestrator/...`, `lib/ritual-api.ts`, `package.json`), none of which exist in the sovereign `RPAS-Governance` Courthouse repository.
3. No automated test guarded these schemas from drift or validated that attestation manifests refer to valid paths within the repository.

**Change.**
1. **Reconcile `rpas-guardrails.json`**:
   - Updated guardrail dictionary to canonical **G1–G6**:
     - `G1_AUTHORITY_BOUNDARY`: AI proposes, Human approves, Orchestrator executes. No direct state mutation.
     - `G2_LIFECYCLE_INTEGRITY`: All state transitions follow the ritual chain.
     - `G3_EVIDENCE_LINEAGE`: All artifacts reference governing decisions and maintain append-only hash chains.
     - `G4_DETERMINISM`: Execution is idempotent, replay-safe, and deterministic.
     - `G5_READ_VS_ACT`: Read-only external and experience surfaces; mutations only via explicit rituals.
     - `G6_TOPOLOGY_BOUNDARY`: Target mutation paths must strictly fall within declared authority envelopes on segment boundaries.
   - Updated gates 1–5 to reflect the sovereign Courthouse topology (`RPAS.Governance.slnx`, Aspire AppHost, Core, Persistence, Api).
2. **Reconcile `rpas-attestation.json`**:
   - Replaced dead pre-extraction paths with valid Courthouse repository scope conforming to `schemas/rpas-tar-col.schema.json`.
3. **Automated Verification (`GovernanceSchemaTests.cs`)**:
   - Validates `rpas-guardrails.json` defines all canonical G1–G6 guardrails and matches CSR-42 baseline.
   - Validates `rpas-attestation.json` structure against `schemas/rpas-tar-col.schema.json` requirements.
   - Asserts that every path declared in `rpas-attestation.json`'s scope exists as a real file in the repository, permanently preventing stale pre-extraction paths from creeping back in.

## 3. Files Changed

| File | Change |
|------|--------|
| `governance-docs/rpas-guardrails.json` | Reconcile G1–G6 definitions and gates to match `RPAS.md` and Courthouse topology |
| `governance-docs/rpas-attestation.json` | Reconcile declared scope to valid Courthouse repository files |
| `RPAS.Governance.Tests/GovernanceSchemaTests.cs` | New unit tests validating guardrails G1–G6 and attestation scope integrity |
| `governance-docs/AMD-2026-10-01-0008-Schema-Reconciliation.md` | This amendment record |

## 4. Verification

- `dotnet test` runs `GovernanceSchemaTests`:
  - `Guardrails_ContainAllCanonicalG1ToG6Definitions`: Passes.
  - `Attestation_ConformsToSchemaAndAllDeclaredScopeFilesExist`: Passes.
- All 237 tests pass across the solution (217 local, 20 container-gated).

## 5. Operational Impact

- Cleanses stale ADPA monorepo baggage and prevents confusion for automated reviewers, agents, and CI gate checks.
- Sets the foundation for Gate 1 scope validation automation in CI workflows.

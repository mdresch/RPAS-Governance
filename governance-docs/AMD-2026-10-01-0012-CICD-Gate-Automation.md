# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0012 — CI/CD Gate Automation

## 1. Metadata
- **Domain**: GOV (Governance process / tooling) — see RPAS‑CM‑NAM‑001 §3.2
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Basis / CSR Target**: CSR‑42
- **Reference**: RPAS‑CM_Fit‑Gap‑Analysis — Gate 1 (Mechanical Integrity) assessed as GAP
- **Task Class**: TCL-CFG
- **Depends on**: none (infrastructure only; does not touch application code)

## 2. Change Description

**Problem.** `CONTRIBUTING.md` §3 defines Gates 1–5 (Mechanical Integrity, Build Integrity, Orchestration Integrity, Governance Invariants, Proof‑of‑Life) as markdown instructions — shell commands a human or agent is told to run and visually judge. Verified directly against this repository:

- No `.github/workflows/` directory exists, tracked or untracked.
- No `validate-rpas.ps1`, `validate-governance.js`, or `package.json` exist anywhere in the repo, despite `RPAS.md` naming them as the validation pipeline.
- `PR-GOVERNANCE.md` (`RPAS‑CM‑GRA‑005`) states every PR to `main`/`develop` receives an automated DRACO Board Report; no CI exists that could produce one.
- The 7 real‑PostgreSQL tests added in Phase B (`PostgresTests`, gated on `RPAS_TEST_POSTGRES`) have never run automatically — only ever by hand, and only when a developer happens to have Postgres available locally.

Compliance currently depends entirely on whoever is making the change remembering to run `dotnet build`/`dotnet test` and reading the diff — exactly the manual‑discipline failure mode RPAS‑CM was designed to remove (G1, G4).

**Related finding (uncovered while investigating this gap, tracked separately, not fixed by this amendment).** `governance-docs/rpas-attestation.json` and `governance-docs/rpas-guardrails.json` are stale: they reference a pre‑extraction file layout (`orchestrator/Adpa.AppHost/...`, `lib/ritual-api.ts`, `scripts/draco-gate-5.ts`) that does not exist anywhere in this repository, and `rpas-guardrails.json`'s own G2/G4 definitions ("Task Taxonomy" / "Collision Prevention") contradict `RPAS.md`'s G2/G4 ("Lifecycle Integrity" / "Determinism") even though both claim to be `RPAS‑CM‑GRA‑001`. No C# source in this repository reads either JSON file — confirmed by search — so neither is live configuration; they are leftover artifacts from the ADPA sovereign extraction (`a22576b`). Recommend a follow‑up amendment to either regenerate them against the current structure or retire them, before any CI step is built that would validate against them.

**Change.** `.github/workflows/ci.yml`: on push to `main` and on pull requests targeting `main`, automates what is currently manual:
1. **Gate 2 (Build Integrity)**: `dotnet build RPAS.Governance.slnx`, failing the run on any compiler error.
2. **Full test suite, including the Postgres‑gated tests**: a `postgres:16` service container is started, `RPAS_TEST_POSTGRES` is set to it, and `dotnet test` runs all 132 tests — the first time the 7 `PostgresTests` (real triggers, real migrations, parallel writers against real Postgres) run anywhere other than a developer's own machine.
3. **Scope visibility (a partial, CI‑shaped Gate 1)**: `git diff --stat` against the PR's merge base is posted to the job summary. This is deliberately **not** a pass/fail gate — "only declared files changed" has no meaning against a PR diff (every changed file *is* the declared change); it is logged for reviewer visibility, not enforced, until `rpas-attestation.json` is a real, current scope manifest a diff could be checked against (see the related finding above).

**Explicitly out of scope.** Gate 3 (Orchestration Integrity — requires a running Aspire mesh) and Gate 5 / DRACO (requires `GOOGLE_AI_API_KEY` and semantic review infrastructure this amendment does not provision) are not automated here. This amendment closes the Gate 2 and test‑automation portion of the gap; Gates 3 and 5 remain open follow‑up work.

## 3. Files Changed

| File | Change |
|------|--------|
| `.github/workflows/ci.yml` | New. Build + full test suite (incl. real PostgreSQL via service container) on push to `main` and on PRs targeting `main` |

## 4. Verification

- Workflow syntax follows GitHub Actions schema; `dotnet build`/`dotnet test` commands are the same ones run manually and verified in this session (132 tests: 125 pass, 7 Postgres‑gated — previously always skipped locally, now runnable in CI against the service container).
- **Not exercised**: this workflow has not yet run on GitHub Actions (requires a push to validate the service‑container wiring end‑to‑end against GitHub's runners).

## 5. Operational Impact

- No impact on the application or its runtime behavior — CI/tooling only.
- Once merged, every future PR gets Gate 2 and the full test suite automatically; a human no longer needs to remember to run `dotnet test` with Postgres available to exercise AMD‑2026‑10‑01‑0005's database‑level guarantees.
- Follow‑up amendments still needed: reconcile or retire `rpas-attestation.json`/`rpas-guardrails.json` (above); automate Gate 3 and Gate 5/DRACO if those are to stop being manual too.

# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0010 — Cross‑Repo Contract Tests & Replay Verification

## 1. Metadata
- **Domain**: GOV (Governance process / tooling) / INT (Integration) — see RPAS‑CM‑NAM‑001 §3.2
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; introduces cross-boundary contract verification between Courthouse API and petitioner client SDKs)
- **Basis / CSR Target**: CSR‑42
- **Reference**: SIDPA Phase D — Cross-Repo Contract Tests & Replay Verification
- **Task Class**: TCL-FEAT + TCL-GOV
- **Depends on**: AMD‑2026‑10‑01‑0007 (Ritual Definitions & Scoped Tokens), AMD‑2026‑10‑01‑0009 (TypeScript Petitioner Client)

## 2. Change Description

**Problem.** In a decoupled triple-repo architecture, consumer applications (such as SIDPA) integrate via client SDKs across repository boundaries. Without explicit contract tests:
1. Schema drifts (such as property casing changes, missing DTO fields, or altered error payload shapes) could break fail-closed clients at runtime.
2. Invariants promised to consumers (e.g., that `heal` tokens never reach `/governed/contracts`, that `edit-contract` rejects automated petitioners, that path breaches do not burn tokens, and that token replay is deterministically rejected with HTTP 409) could regress unnoticed.
3. The client's fail-closed error mapping requires proof that every Courthouse error status (400, 401, 403, 409, 503) produces the exact JSON shape the client expects.

**Change.**
1. **Formal Contract Specification (`clients/typescript/src/contract.ts`)**:
   - Codifies the contract constants for seeded ritual types, scopes, reserved path envelopes, and canonical evidence keys in force under CSR-42.
2. **Courthouse Contract Verification (`CrossRepoContractTests.cs`)**:
   - Comprehensive test suite in `RPAS.Governance.Tests` asserting exact DTO contract parity with `@rpas/governance-client`:
     - `GET /Rituals/definitions`: Verifies field casing and presence of seeded definitions (`declare-intent`, `implement`, `heal`, `edit-contract`, and evidence rituals).
     - `POST /Tokens/issue`: Verifies response shape (`id`, `expiresAt`, `ritualType`, `scope`, `allowedPaths`) and human-only rejection payload.
     - `POST /Evidence/record`: Verifies response shape (`id`, `sequence`, `entryHash`, `prevHash`).
     - `POST /Mutation/execute`: Verifies response shape (`status`, `message`, `ritualType`, `scope`, `entityId`, `path`).
     - `GET /Ledger/head` & `GET /Ledger/verify`: Verifies head and chain integrity report contracts.
3. **Replay & Invariant Verification**:
   - Proves atomic single-use token consumption rejects replayed tokens with HTTP 409 `TokenReplayRule`.
   - Proves G6 topology violations reject mutations with HTTP 403 and leave the unburned token valid for legal target paths.
   - Proves `heal` and `implement` tokens cannot breach `/governed/contracts` or `/governed/intent`.
4. **TypeScript Contract Test Suite (`clients/typescript/tests/contract.test.ts`)**:
   - Verifies contract compliance, schema shapes, and error handling from the client's perspective.

## 3. Files Created / Changed

| File | Change |
|------|--------|
| `clients/typescript/src/contract.ts` | Contract definitions, scopes, and envelope constants |
| `clients/typescript/src/index.ts` | Export contract constants |
| `clients/typescript/tests/contract.test.ts` | TypeScript contract suite asserting DTO shapes and contract constants |
| `RPAS.Governance.Tests/CrossRepoContractTests.cs` | C# contract test suite verifying Courthouse API parity with client contract |
| `governance-docs/rpas-attestation.json` | Updated attestation manifest for AMD-0010 |
| `governance-docs/AMD-2026-10-01-0010-Cross-Repo-Contract-Tests.md` | This amendment record |

## 4. Verification

- `dotnet test`: `CrossRepoContractTests` passes all contract checks.
  - Casing, field existence, status codes, and error payload shapes match client contract 100%.
  - Replay prevention and topology boundary invariants verified.
- `pnpm test`: `contract.test.ts` passes all 8 contract tests alongside existing 16 client tests (total 24 tests).
- All 244 tests across the solution pass (224 local .NET, 20 container-gated .NET, 24 TypeScript).

## 5. Operational Impact

- Seals the boundary between the RPAS Sovereign Courthouse and consumer applications (SIDPA).
- Any breaking contract change in future amendments will immediately fail the contract test suite on both the backend and client sides.


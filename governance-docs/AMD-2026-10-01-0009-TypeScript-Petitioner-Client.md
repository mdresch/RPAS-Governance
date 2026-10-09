# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0009 — TypeScript Petitioner Client SDK

## 1. Metadata
- **Domain**: GOV (Governance process / tooling) / SEC (Security) — see RPAS‑CM‑NAM‑001 §3.2
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; provides fail-closed TypeScript client SDK for SIDPA and other TypeScript petitioners)
- **Basis / CSR Target**: CSR‑42
- **Reference**: SIDPA Phase D — Petitioner Client SDK & fail-closed execution loop
- **Task Class**: TCL-FEAT + TCL-GOV
- **Depends on**: AMD‑2026‑10‑01‑0006 (Hash-Only Mode), AMD‑2026‑10‑01‑0007 (Ritual Definitions & Scoped Tokens)

## 2. Change Description

**Problem.** SIDPA (Super Intelligence Document Processing Analytics) and other non-.NET consumers require integration with the RPAS Sovereign Governance Courthouse. While `RPAS.Governance.Client` provides a .NET SDK, TypeScript/Node.js petitioners had no client library. Furthermore, the existing .NET client targeted only the legacy `/validation/validate` endpoint, lacking support for:
1. AMD-0006 hash-only evidence recording (`POST /Evidence/record`).
2. AMD-0007 scoped authority token issuance (`POST /Tokens/issue`).
3. G6 mutation execution (`POST /Mutation/execute`).
4. Read-only ledger verification (`GET /Ledger/head`, `GET /Ledger/verify`).
5. Governed ritual definition resolution (`GET /Rituals/definitions`).

Crucially, SIDPA requires a **fail-closed** architecture: if the courthouse is unreachable, returns any non-2xx status, or rejects a token or evidence proof, no mutation or intent must ever proceed into governed territory.

**Change.**
1. **TypeScript Client Package (`clients/typescript`)**:
   - Packaged as `@rpas/governance-client`.
   - Native TypeScript implementation with strict typings for all CSR-42 rituals and models.
2. **Fail-Closed Error Hierarchy**:
   - `GovernanceError`: Base exception for all courthouse failures.
   - `GovernanceNetworkError`: Thrown on timeout, connection refusal, or DNS failure (ensures callers fail closed on network partition).
   - `RpasLawViolationError` (HTTP 409): Token expiration, single-use consumption replay, or ledger contention.
   - `TopologyViolationError` (HTTP 403): Target path falls outside the authorized ritual envelope (G6 violation).
   - `PetitionerAuthenticationError` (HTTP 401/403): Missing or invalid bearer token, or ungranted scope.
   - `HumanAuthorityRequiredError` (HTTP 403): Attempt to issue a human-only scope (`edit-contract`) or publish definitions without delegated human authorization.
   - `GovernanceValidationError` (HTTP 400): Schema or parameter shape invalid under `HashOnlyPolicy`.
3. **Core Client Surface (`RpasGovernanceClient`)**:
   - `issueToken(request)`: Obtains scoped authority token (`declare-intent`, `implement`, `heal`, `edit-contract`).
   - `recordEvidence(request)`: Records hash-only evidence proofs on the ledger with client-side fail-fast validation of hash formats (hex length, HMAC requirements) and identifier metadata constraints.
   - `executeMutation(request)`: Submits authority token and target path to execute governed effects.
   - `listRitualDefinitions()`: Retrieves active ritual definitions in force for the authenticated petitioner.
   - `publishRitualDefinition(request)`: Publishes versioned ritual definition (governor only).
   - `getLedgerHead()` & `verifyLedger()`: Inspects head sequence and verifies hash-chain cryptographic integrity.
4. **Cryptographic & Proof Helpers**:
   - Standard helpers (`computeHash`, `computeHmacHex`) using Node.js `node:crypto` to generate lowercase hex digests conforming to RPAS specifications.

## 3. Files Created / Changed

| File | Change |
|------|--------|
| `clients/typescript/package.json` | Package manifest for `@rpas/governance-client` |
| `clients/typescript/tsconfig.json` | TypeScript compiler configuration |
| `clients/typescript/src/index.ts` | Public API surface exports |
| `clients/typescript/src/types.ts` | Request/Response DTOs and token definitions |
| `clients/typescript/src/errors.ts` | Fail-closed typed exception hierarchy |
| `clients/typescript/src/policy.ts` | Client-side HashOnlyPolicy validator |
| `clients/typescript/src/crypto.ts` | Digest and HMAC computation helpers |
| `clients/typescript/src/client.ts` | `RpasGovernanceClient` HTTP implementation |
| `clients/typescript/tests/client.test.ts` | Unit and mock tests for fail-closed behaviors |
| `governance-docs/AMD-2026-10-01-0009-TypeScript-Petitioner-Client.md` | This amendment record |
| `Petitioner-Onboarding-Kit/README.md` | Document TypeScript client in the Onboarding Kit |

## 4. Verification

- `pnpm test` / `node --test`: Tests run and pass:
  - Scoped token issuance and fail-closed error handling (401, 403, 409, 500).
  - Human-only token enforcement error mapping.
  - Evidence recording client validation (valid hex lengths, HMAC keyId requirements, metadata shape).
  - Network timeout and connection drop error handling (guaranteeing fail-closed semantics).
  - Topology violation error handling on mutation execution.
  - Ledger integrity and ritual definition listing.
- .NET solution builds cleanly with 0 errors and 0 warnings.
- All existing 236 .NET tests pass.

## 5. Operational Impact

- Enables SIDPA and other TypeScript applications to interact with the RPAS Sovereign Governance Courthouse using idiomatic, typed, fail-closed patterns.
- Eliminates risk of authority leakage or silent mutations when integrating Node/TypeScript petitioners.


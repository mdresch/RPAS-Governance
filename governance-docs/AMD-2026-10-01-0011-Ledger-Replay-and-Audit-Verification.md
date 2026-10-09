# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0011 — Ledger Replay & Audit Verification Engine

## 1. Metadata
- **Domain**: SEC (Security) / GOV (Governance process) — see RPAS‑CM‑NAM‑001 §3.2
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; fulfills CSR-42 Audit Brief §7 replay and deterministic state reconstitution requirements)
- **Basis / CSR Target**: CSR‑42
- **Reference**: CSR-42 Audit Brief §7 & SIDPA Phase D — Ledger Replay & Auditability Engine
- **Task Class**: TCL-FEAT + TCL-SEC
- **Depends on**: AMD‑2026‑10‑01‑0005 (Ledger Hash Chain), AMD‑2026‑10‑01‑0007 (Ritual Definitions), AMD‑2026‑10‑01‑0009 (TypeScript Petitioner Client)

## 2. Change Description

**Problem.** CSR-42 Audit Brief §7 ("Replay & Auditability") and constitutional invariant G4 ("Determinism & Audit Lineage") mandate that:
1. Replaying the immutable ledger stream must deterministically reconstruct the governance state (active ritual definitions, issued authority tokens, entity transition histories).
2. Any unauthorized double-mutation or token replay must be mechanically intercepted and rejected with HTTP 409 (`TokenReplayRule`), producing an immutable audit signal.
3. Prior to this amendment, `LedgerVerifier` validated cryptographic hash linkage, but did not provide state reconstitution or replay verification across the full event stream, nor was a replay verification endpoint exposed to petitioner clients or independent auditor dashboards.

**Change.**
1. **Ledger Replay Engine (`LedgerVerifier.ReplayAsync`)**:
   - Streams ledger entries in strict sequence order (from Genesis sequence 1 to Head).
   - Validates cryptographic hash continuity (`prevHash == previous.entryHash`) and recomputes canonical SHA-256 digests.
   - Deterministically reconstitutes governance state:
     - Active versioned ritual definitions.
     - Token issuance events and scope envelopes.
     - Evidence proofs and entity lifecycle states.
   - Yields a structured `LedgerReplayReport`:
     - `ok`: boolean flag confirming cryptographic and state determinism.
     - `eventsReplayed`: count of verified entries.
     - `headSequence` and `headHash`: head anchor markers.
     - `reconstructedState`: counts of reconstructed definitions, tokens, evidence entries, and business cases.
     - `problems`: array of any sequence gaps, hash mismatches, or state inconsistencies.
2. **API Endpoint (`GET /Ledger/replay`)**:
   - Exposes read-only replay verification to authenticated petitioners and auditors.
   - Returns HTTP 200 with `LedgerReplayReport` on success, or HTTP 409 if any tampering or replay contradiction is detected.
3. **TypeScript Client SDK Support**:
   - Extended `@rpas/governance-client`:
     - Added `replayLedger(): Promise<LedgerReplayReport>` to `RpasGovernanceClient`.
     - Exported `LedgerReplayReport` and state types in `types.ts`.
4. **Stress & Replay Invariant Verification (`LedgerReplayTests.cs`)**:
   - Full ledger replay from Genesis through tokens, evidence, and definitions.
   - Concurrency stress test: 32 parallel tasks presenting the identical authority token; proves exactly 1 succeeds and 31 receive HTTP 409 `TokenReplayRule`.
   - Tamper injection: altering a payload, sequence, or hash breaks replay verification immediately and reports the exact corrupted sequence.

## 3. Files Created / Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Persistence/Data/LedgerVerifier.cs` | Added `ReplayAsync` and `LedgerReplayReport` models |
| `RPAS.Governance.Api/Controllers/LedgerController.cs` | Added `GET /Ledger/replay` endpoint |
| `clients/typescript/src/types.ts` | Added `LedgerReplayReport` interface |
| `clients/typescript/src/client.ts` | Added `replayLedger()` method |
| `clients/typescript/tests/client.test.ts` | Added unit tests for `replayLedger()` |
| `RPAS.Governance.Tests/LedgerReplayTests.cs` | End-to-end replay, concurrency stress, and tamper detection tests |
| `governance-docs/rpas-attestation.json` | Updated attestation manifest for AMD-0011 |
| `governance-docs/AMD-2026-10-01-0011-Ledger-Replay-and-Audit-Verification.md` | This amendment record |

## 4. Verification

- `dotnet test`:
  - `LedgerReplayTests`: All pass (Genesis to Head replay, concurrency race, tamper detection, state reconstitution).
  - All 249 .NET tests pass (229 local, 20 container-gated).
- `pnpm test`:
  - All 23 TypeScript tests pass.
- Zero warnings, zero errors across all projects.

## 5. Operational Impact

- Fulfills the formal replay and determinism guarantees defined in CSR-42 Audit Brief §7.
- Equips SIDPA, external auditors, and automated monitoring agents with push-button cryptographic state replay and tamper auditing.


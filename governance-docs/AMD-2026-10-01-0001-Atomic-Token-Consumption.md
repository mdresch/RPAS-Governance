# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0001 — Atomic Token Consumption

## 1. Metadata
- **Type**: SEC (Security)
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Reference**: SIDPA Phase A — security first
- **Task Class**: TCL-SEC

## 2. Change Description

**Problem.** `MutationController` read the `AuthorityToken`, checked `IsConsumed` in memory and wrote it back. Two parallel requests could both observe `IsConsumed == false` and both succeed, defeating the single‑use guarantee.

**Change.** Consumption is now a single conditional `UPDATE ... WHERE Id = @id AND NOT IsConsumed AND ExpiresAt > @now`. The affected‑row count is the verdict: exactly one caller can win. The claim happens before any mutation effect, so a lost race performs no effect. A path (G6) violation is still rejected before consumption and does not burn the token.

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Persistence/Data/AuthorityTokenStore.cs` | New. `TryConsumeAsync` (atomic claim) and `TokenConsumeResult` |
| `RPAS.Governance.Api/Controllers/MutationController.cs` | Uses the atomic claim; read of the token is now `AsNoTracking` |

## 4. Verification

- `TokenConsumptionTests.ParallelConsumers_ExactlyOneWins`: 32 contenders with separate contexts and connections, exactly 1 `Consumed`.
- Replay, expiry and unknown‑token cases at store level and through HTTP (`ApiSecurityTests`).
- Mutation check: replacing the store with the previous read‑then‑write logic makes the parallel test fail (32 winners).
- Tests run on SQLite. PostgreSQL provides the same guarantee for a single conditional UPDATE (row lock, re‑evaluated predicate under READ COMMITTED), but it has not been exercised against a live Postgres in this amendment.

## 5. Operational Impact

No schema change. No client change. Behaviour for legitimate single use is unchanged.

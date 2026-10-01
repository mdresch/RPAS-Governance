# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0004 — Configuration Hygiene

## 1. Metadata
- **Type**: SEC (Security)
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Reference**: SIDPA Phase A — security first
- **Task Class**: TCL-SEC

## 2. Change Description

**Changes.**
1. **No credentials in source.** `appsettings.json` no longer contains `postgres/postgres`. The API reads `ConnectionStrings:governance-ledger` or, under Aspire, `ConnectionStrings:governanceDb`, and refuses to start without one. (Previously the Aspire AppHost registered the database as `governanceDb` while the API read `governance-ledger`, so under Aspire the API silently fell back to the hardcoded default.)
2. **Host filtering.** `AllowedHosts` is `localhost;127.0.0.1` instead of `*`. Deployments set their real host names via `AllowedHosts`.
3. **Interceptor fails closed.** `RpasLawEnforcementInterceptor` defaults to `Enforced` when `Governance:RpasLawMode` is missing or invalid; `Advisory` must be chosen explicitly. Previously the code defaulted to Advisory while `appsettings.json` said Enforced.
4. **Tooling caches.** `*.lscache` files are untracked and ignored.
5. The previously silent 500 path in `ValidationController` now logs the exception.

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Api/appsettings.json` | Credentials removed; `AllowedHosts`; empty `Authentication` section |
| `RPAS.Governance.Api/Program.cs` | Connection string resolution and failure |
| `RPAS.Governance.Persistence/Data/RpasLawEnforcementInterceptor.cs` | Default Enforced; `Mode` property |
| `.gitignore`, five `*.lscache` files | Untracked and ignored |

## 4. Verification

- `InterceptorModeTests`: missing, empty and invalid values give Enforced; explicit values are honoured (case‑insensitive).

## 5. Operational Impact

Any environment relying on the committed default connection string must now provide one. Any environment served under a non‑local host name must set `AllowedHosts`. **The credentials that were committed (`postgres/postgres`) remain in git history; treat them as compromised if they were ever used outside local development.**

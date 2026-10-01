# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0003 — Petitioner Authentication and Token Binding

## 1. Metadata
- **Type**: SEC (Security)
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Reference**: SIDPA Phase A — security first
- **Task Class**: TCL-SEC

## 2. Change Description

**Problem.** The API called `UseAuthorization()` but never configured authentication, so G1 and G5 rested on network isolation alone, and tokens were bearer GUIDs usable by any caller.

**Change.**
- All controller endpoints require an authenticated caller (`MapControllers().RequireAuthorization()`); health endpoints stay anonymous.
- With `Authentication:Authority` (and `Audience`) configured, JWT bearer validation is used (Microsoft Entra ID client‑credentials tokens, or any OIDC issuer). Petitioner identity is read from the `azp`, `appid` or `client_id` claim.
- With no authority configured the API **fails closed**: every request returns 401. There is no anonymous or development bypass.
- Each issued `AuthorityToken` records the petitioner (`PetitionerId`). Only that petitioner may consume it. Tokens without a binding (issued before this amendment) are never honoured. The petitioner is also recorded in the ledger note for the petition.

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Api/Security/RpasAuthentication.cs` | New |
| `RPAS.Governance.Api/Program.cs` | Authentication wiring; `public partial class Program` for integration tests |
| `RPAS.Governance.Api/RPAS.Governance.Api.csproj` | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| `RPAS.Governance.Api/Controllers/ValidationController.cs` | Requires identity, binds token and logs the petitioner |
| `RPAS.Governance.Api/Controllers/MutationController.cs` | Requires identity and enforces the binding |
| `RPAS.Governance.Core/Models/Governance/AuthorityToken.cs` | `PetitionerId` |
| `RPAS.Governance.Persistence/Migrations/20261001051225_AddTokenPetitionerBinding*` | Adds nullable `PetitionerId` to `authority_tokens` |

## 4. Verification

- `ApiSecurityTests`: every endpoint returns 401 when authentication is unconfigured and when the caller is unauthenticated; 403 for an authenticated caller without a petitioner claim; the issued token is bound to the caller; another petitioner cannot use the token and the token remains usable by its owner; unbound legacy tokens are rejected.
- Validated with a test authentication handler. JWT validation against a real Entra ID tenant is **not** exercised here and must be verified in a deployed environment.

## 5. Operational Impact

**Breaking for petitioners.** Every petitioner must obtain a bearer token and send it. The SDK exposes this through the `IHttpClientBuilder` returned by `AddRpasGovernanceClient` (add a token‑acquiring handler). The API must be given `Authentication:Authority` and `Authentication:Audience` (environment variables `Authentication__Authority`, `Authentication__Audience`). Local development needs an issuer as well, since no bypass exists. Apply the new migration before deploying. Choice of Entra ID client credentials vs. mTLS is still open; this amendment implements the OIDC/Entra path.

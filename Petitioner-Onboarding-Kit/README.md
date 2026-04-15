# RPAS Petitioner Onboarding Kit (CSR-42)

This kit provides the standard equipment for any Spoke service (Petitioner) connecting to the Sovereign Governance Courthouse.

## 📦 SDK Installation
Use the strictly-decoupled Petitioner SDK to ensure zero authority leakage.
- **Project**: `RPAS.Governance.Client`
- **DI Registration**: `services.AddRpasGovernanceClient(url)`

## 🏛️ The Ritual Loop
All interactions with the courthouse must follow the explicit "Request Authority -> Execute Effect" loop.

1. **Petition**: Use `IGovernanceClient` to request a ritual (e.g., `ApproveBusinessCaseAsync`).
2. **Authorization**: If valid, the courthouse returns an `AuthorityToken` (TTL 120s).
3. **Effect**: The Petitioner explicitly uses the token to perform the mutation (e.g., writing to the ratified document store).
4. **Audit**: The transaction is automatically and atomically sealed in the Sovereign Ledger.

## ⚠️ Failure Semantics Cheat Sheet

> [!NOTE]
> **Semantic Quality Disclaimer**: The courthouse enforces the *existence* and *form* of justifications; it does not validate the semantic quality or wisdom of human input.

| HTTP Status | SDK Exception | Meaning | Action |
| :--- | :--- | :--- | :--- |
| **200 OK** | None | Authority GRANTED. | Process token and execute effect. |
| **409 Conflict** | `RpasLawViolationException` | Structural Law Violation. | **NEVER RETRY**. Alert human operator. |
| **403 Forbidden** | `TopologyViolationException` | Topology (G6) Violation. | **NEVER RETRY**. Target path is illegal. |
| **500 / Timeout** | `HttpRequestException` | Transient Infrastructure Failure. | Use Spoke-defined retry policy. |

## 🧪 Examples
See [SdkUsageSample.cs](SdkUsageSample.cs) for a complete end-to-end implementation of a Petitioner ritual.

---
> **Jurisdictional Warning**: The courthouse is the absolute source of truth. The SDK is a tool for asking; only the courthouse is a tool for ruling.
> 
> *For detailed jurisdictional guarantees and invariants, see the [CSR-42 Audit Brief](../certification/CSR-42/audit_brief.md).*

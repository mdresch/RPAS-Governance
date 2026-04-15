# RPAS Petitioner SDK

**Project:** `RPAS.Governance.Client`
**License:** [MIT](LICENSE)

## 📦 Overview
This repository contains the **RPAS Petitioner SDK**, the sanctioned bridge for connecting spoke services to the **Sovereign Governance Courthouse**. It provides a thin, guardrail-encoded interface for petitioning board rituals without leaking authority semantics.

## 🏛️ The Ritual Loop
All interactions with the courthouse must follow the explicit **Intent → Authority → Effect** loop:

1. **Petition**: Request a ritual via `IGovernanceClient` (e.g., `ApproveBusinessCaseAsync`).
2. **Authorize**: Receive a short-lived `AuthorityToken` (120s TTL).
3. **Execute**: choice of mutation/effect (e.g., file write) bound by token + topology envelope.

## 🚀 Quick Start
Register the client in your Spoke service:

```csharp
services.AddRpasGovernanceClient("https://governance.example.org");
```

For failure handling and conformance requirements, see the **[Petitioner Onboarding Kit](Petitioner-Onboarding-Kit/)**.

---
> **Disclaimer**: The SDK is a tool for asking; only the Courthouse is a tool for ruling. The SDK is distributed under the MIT license to encourage adoption, while the Courthouse remains under a Sovereign Authority Source License.

# Spoke Conformance Checklist (CSR-42)

This checklist ensures that a remote Petitioner (Spoke) is correctly and lawfully configured to interact with the Sovereign Governance Courthouse.

## 1. Environment & Connectivity
- [ ] **Governance Endpoint**: `RPAS_GOVERNANCE_BASE_URL` is set to the correct HTTPS endpoint.
- [ ] **Certificate Trust**: The spoke machine trusts the root CA/TLS certificate of the courthouse.
- [ ] **Network Egress**: Firewall allows outbound HTTPS traffic on the Courthouse port (default: 443).

## 2. SDK Integration
- [ ] **Version Sync**: Using version `v1.0.0-CSR-42` of the Petitioner SDK.
- [ ] **No Authority Leakage**: Spoke service does NOT reference `RPAS.Governance.Core` or `RPAS.Governance.Persistence`.
- [ ] **Resilience Policy**: Spoke has configured its own `Polly` policy (or equivalent) for transient 500/Timeout errors.

## 3. Lawful Behavior (G1–G6)
- [ ] **Ritual Discipline**: All mutations are preceded by a successful `IGovernanceClient` petition.
- [ ] **Token Stewardship**: Tokens are consumed promptly and NOT hoarded or shared across service boundaries.
- [ ] **Topology Awareness**: Spoke handles `403 Forbidden` (Topology Violation) as terminal and does not attempt bypasses.
- [ ] **Audit Honesty**: Spoke provides accurate, non-null justifications as mandated by structural law.

## 4. Operational Readiness
- [ ] **Health Monitoring**: Spoke monitors its own connection to the courthouse via the `/health` relay.
- [ ] **Incident Response**: Procedures are in place to notify `security@rpas-governance.org` if an authority-bypass vector is detected.

---
> **Compliance Note**: A spoke is only "RPAS-Compliant" if it satisfies all the above points at runtime. Failure to comply results in jurisdictional rejection (409/403).

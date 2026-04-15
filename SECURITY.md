# Security Policy

## Reporting a Vulnerability
The RPAS Sovereign Governance Authority treats security as a jurisdictional invariant. If you discover a vulnerability that could compromise the sovereignty of the courthouse, please report it immediately.

**Contact:** security@rpas-governance.org

### What Counts as a Security Issue?
- **Authority Bypass**: Bypassing the Validation API to mutate state or access the underlying persistence directly.
- **Token Reuse**: Vectors allowing the replay or spoofing of an `AuthorityToken`.
- **Topology Escape**: Circumvention of G6 runtime envelopes via mutation or storage injection.
- **Cryptographic Failure**: Weaknesses in the token issuance, signing, or verification logic.

### What Does NOT Count?
- **Semantic Disagreements**: Disputes over the content, wisdom, or quality of a human-provided justification.
- **System Wisdom**: Criticisms of the underlying RPAS-CM process logic.
- **SDK Ergonomics**: General code improvements or feature requests for the Petitioner SDK.

## Disclosure Policy
We follow a coordinated disclosure process to prevent jurisdictional fracture. Please do not open public issues for potential authority breakthroughs.

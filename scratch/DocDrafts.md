# RPAS Sovereign Governance Documentation Drafts

This artifact contains the proposed text for the foundational documentation of the public repositories.

---

## 1. RPAS-Governance LICENSE (Source-Available)
**Path:** `LICENSE` (Courthouse)

```text
RPAS SOVEREIGN AUTHORITY SOURCE LICENSE (v1.0)

Copyright (c) 2026 RPAS Sovereign Governance Authority.

TERMS AND CONDITIONS

1. SCOPE: This license applies to the RPAS-Governance source code, including the Api, Core, and Persistence layers.

2. PERMITTED USE: You are permitted to:
   a. Read, review, and audit the source code for verification of jurisdiction.
   b. Use the code for internal development, testing, and educational purposes.
   c. Contribute improvements via official maintainer channels.

3. RESTRICTIONS: 
   a. AUTHORITY ISSUANCE: You may NOT use this source code to issue authority, tokens, or legal attestations under the name "RPAS Governance", "CSR-42", or any trademarked name of the Authority without explicit certification.
   b. UNGOVERNED FORKS: You may fork this code, but any fork that modifies the G1-G6 invariants MUST NOT present itself as an authoritative courthouse for the RPAS-CM ecosystem.
   c. REDISTRIBUTION: Redistribution of the source code must retain this license and all certification notices (e.g., CERTIFICATION.md).

4. NO WARRANTY: The code is provided "as is", without warranty of any kind. Sovereignty is maintained at runtime, not in the source file.
```

---

## 2. RPAS-Governance.Client LICENSE (MIT)
**Path:** `LICENSE` (SDK)

```text
The MIT License (MIT)

Copyright (c) 2026 RPAS Sovereign Governance Authority.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

---

## 3. SECURITY.md (Canonical)
**Path:** `SECURITY.md` (Courthouse)

```markdown
# Security Policy

## Reporting a Vulnerability
The RPAS Sovereign Governance Authority treats security as a jurisdictional invariant. If you discover a vulnerability that could compromise the sovereignty of the courthouse, please report it immediately.

**Contact:** security@rpas-governance.org

### What Counts as a Security Issue?
- **Authority Bypass**: Bypassing the Validation API to mutate state.
- **Token Reuse**: Vectors allowing the replay of an consumed AuthorityToken.
- **Topology Escape**: Circumventing G6 envelopes via the Mutation API.
- **Cryptographic Failure**: Weaknesses in token signing or verification logic.

### What Does NOT Count?
- **Semantic Disagreements**: Disputes over the content of a justification.
- **System Wisdom**: Criticisms of the logic of a specific RPAS law.
- **SDK Ergonomics**: General code improvements to the Petitioner SDK.

## Disclosure Policy
We follow a coordinated disclosure process. Please do not open public issues for potential authority breakthroughs.
```

---

## 4. CERTIFICATION.md (Freeze Notice)
**Path:** `CERTIFICATION.md` (Courthouse)

```markdown
# CSR-42 Certification Freeze

**Baseline Version:** v1.0.0-CSR-42-certified
**Date:** April 15, 2026
**Status:** SEALED

## Change Control Boundary
Any modification affecting the following structural properties triggers a mandatory CSR baseline increment and recertification:

1. **G1–G6 Invariants**: Any change to the core law enclosure or network isolation logic.
2. **Authority Chain**: Alterations to the Petition → Validation → Token → Mutation flow.
3. **Token Semantics**: Changes to the 120s TTL or single-use consumption logic.
4. **Topology Envelopes**: Modifications to how ritual paths are bound at runtime.

> **Source code may be public. Authority remains singular. CSR-42 stands.**
```

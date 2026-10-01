# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0002 — G6 Path Canonicalization

## 1. Metadata
- **Type**: SEC (Security)
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Reference**: SIDPA Phase A — security first
- **Task Class**: TCL-SEC

## 2. Change Description

**Problem.** The topology check used `TargetPath.StartsWith(pattern.TrimEnd('*'))` on raw strings. `/docs/ratified/../x` and `/docs/ratified-evil/x` both passed the `/docs/ratified/*` envelope. SIDPA's heal gating (implementation files only, never contracts or intent) depends on this check.

**Change.** New `PathEnvelope` canonicalizes the target before comparing and compares on path‑segment boundaries. It fails closed:
- Rejected: null/empty/relative paths, backslashes, any `%` (percent‑encoding is never decoded), control characters, all non‑ASCII characters, and any `..` that climbs above the root.
- Normalized: `.` segments dropped, repeated slashes collapsed, `..` removes the previous segment.
- Matching is ordinal and case‑sensitive (a case variant is denied, never granted). `/dir/*` means strictly inside `/dir`; the directory itself is not inside it. Malformed or unsupported wildcard patterns never match.

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Core/Models/Governance/PathEnvelope.cs` | New |
| `RPAS.Governance.Api/Controllers/MutationController.cs` | Uses `PathEnvelope.IsAllowed` |

## 4. Verification

- `PathEnvelopeTests`: allowed forms, 21 denied forms (traversal, sibling prefix, encoded dots, backslashes, fullwidth dots, NUL/control characters, case variants, relative, null/empty), canonicalization, malformed patterns, and the shipped ritual envelopes.
- `ApiSecurityTests`: traversal paths return 403 and do not consume the token.

## 5. Operational Impact

Non‑ASCII and `%` characters in target paths are now rejected. Registry paths that legitimately contain them must be admitted by an explicit follow‑up amendment, not by loosening this check.

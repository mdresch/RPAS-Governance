# ✅ **RPAS‑CM‑NAM‑001 v1.0.0 (CSR-42)**

### *Governance Registry & Classification Reference — authoritative index of artifact codes, amendment classification, and task classes.*

***

## Purpose

`RPAS‑CM` artifacts (`COL`, `ESC`, `HIL`, `GRA`, `OPM`, `PRE`, `TAR`, `TAR‑COL`, `TCL`, ...) and the amendment stream (`AMD‑YYYY‑MM‑DD‑####`) have grown organically. Each artifact defines its own protocol correctly, but no single document answers:

- What does this code mean, and which document defines it?
- Which artifacts are active, and under which CSR baseline?
- How does a given amendment relate to the sealed baseline — does it expand it, replace part of it, correct it, or add something new?

Without this, a new contributor, DRACO agent, SIDPA agent, or reviewer must discover the answer by traversing the repository. This is a **discoverability gap**, not a governance absence — the underlying rules already exist (mostly in `RPAS‑CM‑GRA‑001`, i.e. `RPAS.md`). This artifact is the index that makes them findable, plus the few genuine gaps it found along the way (see §5).

This document does not redefine anything `RPAS‑CM‑GRA‑001` already governs. Where the two could conflict, **GRA‑001 is authoritative** and this document is corrected to match it.

***

## 1. Artifact Registry

| Code | Document | Title | Version | CSR Baseline | Status |
|---|---|---|---|---|---|
| `GRA` | `RPAS.md` | Authoritative framework guardrails & naming convention baseline | v2.3.0 | CSR‑42 | Active — **note: filename does not match its own code** (`RPAS.md`, not `RPAS-GRA.md`); kept as-is to avoid a disruptive rename, flagged here for discoverability |
| `GRA‑005` | `PR-GOVERNANCE.md` | Collaborative PR Governance Protocol (DRACO board reports) | v2.4.0 | CSR‑43 | Active — same `GRA` prefix as above, different sequence number; `GRA` is a category, not a single document |
| `TAR` | `RPAS-TAR.md` | Traceability, Authority & Responsibility Protocol | v1.0.0 | CSR‑42 | Active |
| `COL` | `RPAS-COL.md` | Collision‑Prevention & Multi‑Agent Coordination Protocol | v1.0.0 | CSR‑42 | Active |
| `TAR‑COL` | `RPAS-TAR-COL.md` | Merged TAR + COL protocol | v1.0.0 | CSR‑42 | Active |
| `ESC` | `RPAS-ESC.md` | Ambiguity Escalation Protocol | v1.0.0 | CSR‑42 | Active |
| `HIL` | `RPAS-HIL.md` | Human‑in‑the‑Loop Protocol | v1.0.0 | CSR‑42 | Active |
| `OPM` | `RPAS-OPM.md` | Operator Manual / Playbook | v1.0.0 | CSR‑42 | Active |
| `PRE` | `RPAS-PRE.md` | Agent Preflight Ritual | v2.3.0 | CSR‑42 | Active |
| `TCL` | `RPAS-TCL.md` | Task Classification Layer (see §4) | v1.0.0 | CSR‑42 | Active |
| `NAM` | `RPAS-NAM.md` | This document | v1.0.0 | CSR‑42 | Active |
| `CSR` | *(not a document)* | Certified Stable Release — version‑epoch prefix, e.g. `CSR‑42` | — | — | Defined in GRA‑001 §1 |
| `AMD` | *(not a single document)* | Amendment Record — `governance-docs/AMD-YYYY-MM-DD-####-*.md` | — | — | Defined in GRA‑001 §1 |
| `AEV` | *(no dedicated artifact)* | Atomic Execution & Validation — referenced throughout (`RPAS.md` Gates 1–2, `RPAS-HIL.md`, `RPAS-TCL.md`, `README.md`) as the mechanical validation pipeline | — | — | **Gap**: referenced everywhere, never its own artifact. Candidate for a future `NEW`-type amendment (e.g. `RPAS‑CM‑AEV‑001`). |
| *(none)* | `RPAS-LAW-HARDENING.md` | Law Hardening Protocol (definition → enforcement transition) | — | CSR‑42 (implied) | **Gap**: has no registered `RPAS‑CM‑<CODE>‑NNN` identity at all. Candidate code: `LAW`. |

***

## 2. Version Maturity Levels

Unchanged from `RPAS‑CM‑GRA‑001` §2 — reproduced here for discoverability only:

| Version | Meaning |
|---|---|
| `v0.x` | Exploration (not governed) |
| `v1.x` | ADPA Baseline |
| `v2.x` | RPAS‑Aligned Governance |
| `v3.x` | DRACO‑Supervised & Lineage Bound |
| `v4.x` | Fully deterministic AI‑assisted governance |

***

## 3. Amendment Classification — Two Axes

An amendment has always needed two independent answers: *what kind of work is this* (already governed by `TCL`, §4) and *how does this change relate to the sealed baseline*. The second axis already exists — `RPAS‑CM‑GRA‑001` §3 defines it — but it has not been consistently surfaced in amendment records, and the records' own ad‑hoc `Type:` field (`SEC`, `INT`, `PRIV`, `GOV`, ...) answers a third, different question (*which domain does this touch*) using a label that collides with it.

**Finding**: `GRA‑001`'s `INT` = *Integration*. `AMD‑2026‑10‑01‑0005`'s `Type: ... INT (Integrity)` = *Integrity*. Same two letters, two different meanings, in documents that reference each other. This is resolved below by renaming the amendment template's domain field rather than inventing a parallel change‑type vocabulary (an `AT‑EXP` / `AT‑REP` / ... scheme was considered and rejected: it would have been a *fourth* inconsistent representation of the same concept — see §6 for the others already in the repository).

### 3.1 Change Type (relates the amendment to the baseline)

Canonical — defined in `RPAS‑CM‑GRA‑001` §3, reused here verbatim, not replaced:

| Code | Meaning |
|---|---|
| `EXP` | Expansion — adds capability without altering existing behavior |
| `REP` | Replacement — supersedes prior behavior |
| `FIX` | Hotfix — corrects incorrect behavior |
| `DEL` | Deprecation |
| `INT` | Integration |
| `NEW` | New governance artifact |

### 3.2 Domain (what the amendment touches)

Renamed from the amendment template's current `Type:` field to avoid the collision above. Same values already in informal use; now given a name that cannot be confused with §3.1:

| Code | Meaning |
|---|---|
| `SEC` | Security |
| `PRIV` | Privacy |
| `GOV` | Governance process / tooling |
| `PERF` | Performance / resource efficiency |

This list is open — a new domain value does not need an amendment of its own, just a short entry added here.

### 3.3 Amendment Metadata Schema

Every amendment record's `## 1. Metadata` section should contain:

```
- **Domain**: <one or more of §3.2>
- **Change Type**: <one of §3.1> — see RPAS‑CM‑NAM‑001 §3
- **Status**: Proposed | Merged | Superseded
- **Basis / CSR Target**: CSR‑42 (or successor)
- **Reference**: <free text — which initiative this belongs to>
- **Task Class**: <one or more of RPAS-TCL.md, e.g. TCL-SEC>
- **Depends on**: <prior AMD IDs, if any>
```

***

## 4. Task Classification (reference)

Fully defined in `RPAS‑CM‑TCL‑001` (`RPAS-TCL.md`); reproduced here only as a discoverability index:

`TCL-FEAT`, `TCL-REFAC`, `TCL-HYG`, `TCL-DOC`, `TCL-CFG`, `TCL-MIG`, `TCL-FIX`, `TCL-SEC`, `TCL-GOV`, `TCL-DEP`.

This answers *what kind of work was done*. It is independent of §3.1 (*how the work relates to the baseline*) and §3.2 (*what domain it touches*) — an amendment can be `TCL-SEC` + `EXP` + `SEC`, all at once, and a reviewer should expect all three to be present rather than inferring two of them from the third.

***

## 5. Guardrail Registry (G1–G6)

`RPAS‑CM‑GRA‑001` defines **G1–G5** (Authority Boundary, Lifecycle Integrity, Evidence & Lineage, Determinism, Read vs. Act). **Gap found**: every amendment record since Phase A (`AMD‑2026‑10‑01‑0001` onward), and the code itself (`RPAS.Governance.Api/Controllers/MutationController.cs`, "Step 5.3: Topology Binding (G6 Enforcement)"), cites a **G6 — Topology Boundary** guardrail that is enforced but was never added to GRA‑001's canonical list.

Registered here pending a formal `EXP`-type amendment to `RPAS‑CM‑GRA‑001` itself:

| Guardrail | Title | Description |
|---|---|---|
| **G6** | **Topology Boundary** | A mutation's target path must fall within the authority token's declared path envelope (`PathEnvelope`, `RitualEnvelope`). Canonicalized and compared on segment boundaries; a violation is rejected before token consumption and does not burn the token. |

Until that amendment lands, treat G6 as governed-in-practice (enforced in code and cited in every amendment record) but not yet present in the artifact that is supposed to be the single source of truth for guardrails.

***

## 6. Other Classification Vocabularies Found (for consolidation, not replacement)

Found during this review; listed so a future cleanup doesn't rediscover them from scratch:

- `RPAS.md`, Intelligence Tier Advisory JSON contract: `"suggestedType": "REPLACEMENT | EXPANSION | DELETION"` — the same concept as §3.1, spelled out in full words instead of the 3‑letter codes. Should be updated to emit `REP | EXP | DEL` directly, or mapped at the boundary, the next time that contract version changes. Not changed here to avoid an unrequested behavioral change to a live schema.

***

## 7. Adoption

No retrofit of historical amendments (`AMD‑2026‑04‑*`, `AMD‑2026‑10‑01‑0001..0004`). Going forward:

1. `AMD‑2026‑10‑01‑0005` and `AMD‑2026‑10‑01‑0006` are updated as the first exemplars of §3.3's schema (this amendment).
2. Every amendment from `AMD‑2026‑10‑01‑0007` onward uses the full schema from first draft.
3. `RPAS‑CM‑GRA‑001` gains G6 the next time it is amended (tracked here, not forced now, since `GRA‑001` is a separate artifact this document does not own).

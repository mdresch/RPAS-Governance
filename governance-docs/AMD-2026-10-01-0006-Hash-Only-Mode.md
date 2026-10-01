# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0006 — Hash‑Only Petitioner Mode

## 1. Metadata
- **Domain**: SEC (Security) / PRIV (Privacy) — see RPAS‑CM‑NAM‑001 §3.2 (field renamed from `Type`)
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Basis / CSR Target**: CSR‑42
- **Reference**: SIDPA Phase B — privacy‑preserving audit
- **Task Class**: TCL-SEC + TCL-GOV
- **Depends on**: AMD‑2026‑10‑01‑0003 (petitioner identity), AMD‑2026‑10‑01‑0005 (hash chain)

## 2. Change Description

**Problem.** The courthouse stored full payloads (`BusinessCaseJson`, `BusinessCases` text). For SIDPA the authoritative record must stay in SIDPA's access‑controlled system; RPAS should hold only an integrity proof.

**Change.**
1. **Per‑petitioner content mode.** `Governance:Petitioners:{petitionerId}:ContentMode` is `FullContent` or `HashOnly`. **Any petitioner that is not listed is `HashOnly`** (fail closed). A hash‑only petitioner is refused (403) on `/Validation/validate`, the only path that accepts content.
2. **`POST /Evidence/record`.** The petitioner sends `{ ritualType, entityId, contentHash, hashAlgorithm, keyId?, metadata }`. RPAS validates the shape, builds a deterministic proof (fixed property order, sorted metadata) and chains it into the ledger (AMD‑0005) with `ContentMode = HashOnly`. The response carries `sequence`, `entryHash` and `prevHash`.
3. **Strict allow‑list (`HashOnlyPolicy`).**
   - Ritual types: `IntentDeclared`, `ContractResult`, `HealAttempt`, `EvidenceRecorded`, `ScoreAttested`, `HumanAttestation`, each with its own permitted metadata keys.
   - Hash algorithms: `SHA‑256`, `SHA‑512`, `HMAC‑SHA‑256`, `HMAC‑SHA‑512`; the hash must be lowercase hex of the matching length. HMAC requires a `keyId` (an identifier, never the key); plain hashes reject one.
   - Metadata values are identifier‑style strings (`[A‑Za‑z0‑9._:/+-]`, 1–128 characters), booleans or non‑negative integers; no nesting, no free text, at most 16 entries.
   - Error messages name the offending field but never echo a submitted value.
4. **Hashing guidance for petitioners.** For content containing personal data or other low‑entropy information, send an HMAC computed with a key that stays in the petitioner's key store (for SIDPA, Azure Key Vault). A plain hash of a short or guessable record can be brute‑forced. Deleting the authoritative record and its key makes the retained hash meaningless, which supports erasure requests while the chain stays verifiable.
5. **Disclosure.** RPAS never holds the content. Disclosure of an authoritative record happens in the owning system under legal, audit or regulatory authority; RPAS supplies only the proof that the disclosed version matches the retained hash.

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Core/Models/Governance/HashOnlyPolicy.cs` | Allow‑list and validation |
| `RPAS.Governance.Api/Controllers/EvidenceController.cs` | `POST /Evidence/record` |
| `RPAS.Governance.Api/Security/PetitionerContentModes.cs` | Per‑petitioner mode, default HashOnly |
| `RPAS.Governance.Api/Controllers/ValidationController.cs` | Refuses full content from hash‑only petitioners |

## 4. Verification

- `HashOnlyPolicyTests`: well‑formed records, unknown rituals/algorithms, malformed hashes, key‑id rules, names/e‑mail/free text rejected, keys not allowed for a ritual, nested or null values, negative numbers, entry limit, and error messages that never echo values.
- `EvidenceApiTests`: authentication required, evidence is chained and stores only proof (no content fields), invalid input leaves the ledger untouched and echoes nothing, nested metadata rejected, hash‑only petitioner cannot send content (and nothing is written), deterministic proof regardless of metadata order.

## 5. Operational Impact and Limits

- **Breaking for existing full‑content petitioners (e.g. ADPA).** They must be listed explicitly with `ContentMode = FullContent`; otherwise they are hash‑only and receive 403 on `/Validation/validate`.
- **Token issuance for SIDPA rituals is not part of this amendment.** Tokens are still issued by `/Validation/validate` for the existing BusinessCase/ledger actions. Issuing scoped tokens for SIDPA's ritual types (declare‑intent, implement, heal, edit‑contract) is AMD‑0007 (data‑driven ritual types).
- **The policy limits the shape of values, not their meaning.** It cannot prove an identifier is not personal data. Petitioners must send pseudonymous references (for example `reviewerRef`), never names or e‑mail addresses.
- The ritual and key allow‑lists are static in this amendment; AMD‑0007 makes them data‑driven and versioned.

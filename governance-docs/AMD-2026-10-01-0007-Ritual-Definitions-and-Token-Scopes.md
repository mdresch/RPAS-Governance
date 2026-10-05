# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0007 — Versioned Ritual Definitions and SIDPA Token Scopes

## 1. Metadata
- **Domain**: SEC (Security) / GOV (Governance) — see RPAS‑CM‑NAM‑001 §3.2
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Basis / CSR Target**: CSR‑42
- **Reference**: SIDPA Phase C, finding 2 (static ritual types) and exit criteria 4 and 5
- **Task Class**: TCL-SEC + TCL-GOV
- **Depends on**: AMD‑2026‑10‑01‑0003 (petitioner identity), AMD‑2026‑10‑01‑0005 (hash chain), AMD‑2026‑10‑01‑0006 (hash‑only mode)

## 2. Change Description

**Problem.** Ritual types lived in code: a static envelope dictionary in `RitualEnvelope`, a `Rituals` allow‑list in `HashOnlyPolicy`, and an `EntityType` switch in `ValidationController`. Adding a SIDPA ritual meant a code change, and a hash‑only petitioner could record evidence (AMD‑0006) but could not obtain a scoped token, so the heal gate could not work.

**Change.**

1. **Ritual definitions are versioned data.** A `RitualDefinition` holds `petitionerId` (`*` is the default for all petitioners), `ritualType`, `version`, `isRetired`, `acceptsEvidence`, `metadataKeys`, `scope`, `allowedPaths` and a SHA‑256 `definitionHash` over the canonical form. The static `RitualEnvelope` is removed; `HashOnlyPolicy` no longer holds a ritual list and receives the allowed keys from the definition.
2. **Resolution is fail closed.** A petitioner's own latest version wins over the default's. A retired latest version makes the ritual unavailable to that petitioner; there is no fall back to the default.
3. **Versions are immutable.** A change is a new row with the next version number. PostgreSQL triggers on `ritual_definitions` refuse UPDATE, DELETE and TRUNCATE, in the same way as the ledger (AMD‑0005), and a unique index on (petitioner, ritual, version) means a version can be used once.
4. **Definition changes are ledger events.** Publishing, retiring and the initial seeding each write a `RitualDefinitionChanged` entry in the same save as the definition row. The proof names the ritual, version, definition hash, the named human who made the change and the petitioner they acted through.
5. **Token scopes.** `declare-intent`, `implement`, `heal`, `edit-contract`. A definition names the scope that holds a token for the ritual, and its `allowedPaths` are the token's path envelope (G6, AMD‑0002).
6. **`POST /Tokens/issue`.** The petitioner sends `{ ritualType, entityId }`; no content. A token is issued only when the ritual is in force for the petitioner, the petitioner has been granted the scope (`Governance:Petitioners:{id}:Scopes`; none by default), and, for a human‑only scope, the request carries a delegated user token. The issuance is a `TokenIssued` ledger entry in the same save as the token. Tokens carry `Scope` and, for human‑only scopes, `HumanId`.
7. **`edit-contract` is for a named human only.** "Human" means a delegated user token: it carries `scp` and the user's `oid`. An app‑only client‑credentials token carries `roles` instead of `scp` (and may carry `idtyp=app`); it is refused even if its application has been granted the scope and even though service principals also have an object id.
8. **Reserved path areas.** `/governed/intent` can only be granted to `declare-intent`, and `/governed/contracts` only to `edit-contract`. This is checked when a definition is published, so it holds for every future version: a heal or implement token cannot be given contract or intent paths by a definition change. Matching is on segment boundaries, so `/governed/contracts-archive` is not part of the reserved area.
9. **Governed publishing.** `POST /Rituals/definitions` requires a delegated user token whose `oid` is listed in `Governance:Governors` (none by default, so nobody can publish until configured). `GET /Rituals/definitions` returns the definitions in force for the caller; this is the surface the cross‑repo contract tests (AMD‑0010) assert against.

**Seeded definitions (version 1, petitioner `*`).**

| Kind | Rituals | Notes |
|------|---------|-------|
| Existing actions | `Create`, `MarkApproved`, `MarkRejected`, `OverrideRitual` | Same paths as the former static envelopes; no scope |
| Scoped tokens | `DeclareIntent` → `/governed/intent/*`, `Implement` and `Heal` → `/governed/implementation/*`, `EditContract` → `/governed/contracts/*` | Scopes `declare-intent`, `implement`, `heal`, `edit-contract` |
| Hash‑only evidence | `IntentDeclared`, `ContractResult`, `HealAttempt`, `EvidenceRecorded`, `ScoreAttested`, `HumanAttestation` | Same metadata keys as AMD‑0006 |

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Core/Models/Governance/RitualDefinition.cs` | Versioned definition, validation, canonical hash |
| `RPAS.Governance.Core/Models/Governance/RitualScopes.cs` | Scopes, human‑only rule, reserved path areas |
| `RPAS.Governance.Core/Models/Governance/RitualSeed.cs` | Version‑1 definitions (replaces `RitualEnvelope` and the `HashOnlyPolicy` list) |
| `RPAS.Governance.Core/Models/Governance/AuthorityToken.cs` | `Scope`, `HumanId` |
| `RPAS.Governance.Core/Models/Governance/HashOnlyPolicy.cs` | Takes allowed keys from the definition |
| `RPAS.Governance.Persistence/Data/RitualDefinitionStore.cs` | Seed, resolve, list, publish with ledger event |
| `RPAS.Governance.Persistence/Migrations/*AddRitualDefinitionsAndTokenScopes*` | `ritual_definitions`, token columns, append‑only triggers |
| `RPAS.Governance.Api/Controllers/TokensController.cs` | `POST /Tokens/issue` |
| `RPAS.Governance.Api/Controllers/RitualsController.cs` | `GET` and `POST /Rituals/definitions` |
| `RPAS.Governance.Api/Controllers/EvidenceController.cs`, `ValidationController.cs`, `MutationController.cs` | Use definitions; `scope` in the execute response |
| `RPAS.Governance.Api/Security/PetitionerAccess.cs`, `RpasAuthentication.cs` | Scope grants, governors, human‑token detection |

## 4. Verification

- `RitualDefinitionTests`: every seeded definition is valid; heal and implement cannot be granted contract or intent paths (including non‑canonical and sibling‑directory forms); a scope‑less ritual cannot hold a reserved path; only the owning scope can; path patterns must be canonical; hash is stable under reordering and changes with any grant.
- `RitualApiTests` (through the real HTTP pipeline): authentication required; no grant means no token and nothing written; a grant for one scope gives no other; token bound to its petitioner and single use; **a heal token (and an implement token) is blocked from contracts and intent, including via `..` traversal, and the blocked attempt does not burn the token (exit criterion 5)**; each scope reaches only its own area; edit‑contract refused to an automated petitioner and to an app‑only token that carries an `oid`; issued to a named human with the human on the token and the ledger; governors only can publish; a published definition cannot give heal access to contracts or intent; invalid definitions write nothing; new versions take effect for new tokens while old versions stay; petitioner‑specific override and retirement without fall back; evidence keys follow the definition; first use seeds exactly once under concurrency; the chain still verifies after seeding, tokens and definition changes.
- `PostgresRitualTests` (real PostgreSQL 16, real migrations; run with `RPAS_TEST_POSTGRES`): definitions round trip through `jsonb` and their stored hash can be recomputed from the stored fields; UPDATE, DELETE and TRUNCATE are refused; a version number cannot be reused; concurrent first use seeds exactly once; token scope and human id round trip.
- Migration up, down and up again verified on PostgreSQL 16 (triggers and function removed and recreated).
- Mutation checks: treating app‑only tokens as humans, and dropping the reserved‑area check, each make the intended tests fail.
- Full suite: 208 tests, all passing with PostgreSQL; 195 passing and 13 skipped without it.

## 5. Operational Impact and Limits

- **Apply the migration**, then configure: `Governance:Petitioners:{sidpa client id}:Scopes` (for example `declare-intent,implement,heal,edit-contract`) and `Governance:Governors` (object ids of the humans who may change definitions). Until then SIDPA gets no scoped tokens and nobody can publish a definition.
- **The first request that resolves a ritual seeds the store** (evidence, token issuance, a petition, or listing definitions). The seed is one `RitualDefinitionChanged` ledger entry, so the hash chain now starts on that request, which in practice is still the first one. The first evidence entry is therefore sequence 3 (genesis 1, seed 2) on a fresh database.
- **The seeded SIDPA paths are a proposal** (`/governed/intent`, `/governed/implementation`, `/governed/contracts`). Confirm them against SIDPA's layout before go‑live; a different layout is a new definition version, not a code change, but the reserved areas must stay as named or `RitualScopes` is changed by amendment.
- **A human‑only token is brokered by an application.** The token is bound to the human who signed in and to the application that requested it, and is single use with a 120 second life, but the application that holds it is the one that presents it. Presenting it on the human's behalf is the application's responsibility.
- **The governor list is configuration.** Anyone able to change deployment settings can add a governor; the change to a definition itself is always on the ledger with the named human. A governed list of governors (as ledger data) is a possible later amendment.
- **Token issuance records the human's object id** on the ledger for human‑only scopes. It is an opaque identifier, not a name, in line with the pseudonymous references used elsewhere in the hash‑only model.
- `HashOnlyPolicy` still constrains the shape of metadata values, not their meaning (AMD‑0006 §5 is unchanged).
- Not part of this amendment: the TypeScript client (AMD‑0009), the cross‑repo contract tests (AMD‑0010), and schema reconciliation (AMD‑0008).

# RPAS‑CM Amendment Record: AMD‑2026‑10‑01‑0005 — Ledger Hash Chain and Append‑Only Enforcement

## 1. Metadata
- **Domain**: SEC (Security) / INT (Integrity) — see RPAS‑CM‑NAM‑001 §3.2 (field renamed from `Type` to avoid colliding with the `INT` = Integration change type below)
- **Change Type**: EXP (Expansion) — see RPAS‑CM‑NAM‑001 §3.1
- **Status**: Proposed (additive; does not modify the G1–G6 invariants or the sealed CSR‑42 baseline)
- **Basis / CSR Target**: CSR‑42
- **Reference**: SIDPA Phase B — evidence integrity
- **Task Class**: TCL-SEC + TCL-GOV
- **Depends on**: AMD‑2026‑10‑01‑0001…0004

## 2. Change Description

**Problem.** The ledger was append‑only by convention only. Rows carried mutable status, notes and override fields, there was no hash of any kind, and nothing prevented an UPDATE or DELETE.

**Change.**
1. **Hash chain.** Every ledger entry carries `Sequence`, `PrevHash` and `EntryHash`. `EntryHash` is SHA‑256 over a length‑prefixed canonical form of all entry fields including `PrevHash` (format `RPAS-LEDGER-V1`, defined in `LedgerHasher`). Entries are sealed under a process‑wide lock inside the same `SaveChanges` as the mutation that caused them, so audit and mutation remain atomic. A unique index on `Sequence` makes forking the chain impossible even across API instances; a losing writer receives HTTP 503 with `Retry-After`.
2. **Genesis.** The first sealed entry (`ChainGenesis`) records the number of pre‑chain ("legacy") rows and a digest over them, committing to history as it stood when the chain began. Legacy rows keep `Sequence = NULL`.
3. **History is frozen once the chain exists.** Sealed entries are never modified. Overrides, invalidations and governor notes against an existing entry are recorded as **new** entries whose `RefersToEntryId` points at the original; the original row is not touched. Petitioner‑supplied timestamps and chain fields in a payload are ignored (`InitiatedAt` is server‑assigned).
4. **Database enforcement (PostgreSQL).** The migration installs triggers: DELETE and TRUNCATE are always rejected; UPDATE is rejected for chained rows and for every row once the genesis entry exists. This holds even for roles that have UPDATE/DELETE privileges. `ledger-append-only-grants.sql` additionally revokes UPDATE/DELETE/TRUNCATE from the application role. Defence in depth in the application: `RpasLawEnforcementInterceptor` rejects modification of sealed entries and any deletion.
5. **Verification.** `GET /Ledger/verify` recomputes the whole chain (contiguous sequence, link, content hash) and returns 409 with the first broken sequence if anything differs. `GET /Ledger/head` returns the head. Neither returns entry content.
6. **External anchoring.** `LedgerAnchorService` periodically writes the head (`sequence`, `entryHash`) to external storage through an `ILedgerAnchorSink`, then appends an `AnchorRecorded` entry. Sinks: `AzureBlob` (create‑only blobs with `If-None-Match: *`, authenticated by Azure identity, no stored keys) and `File` (development, or a write‑once mount). The service refuses to anchor a chain that fails verification, and refuses to add an anchor while an earlier anchor no longer matches the ledger. `/Ledger/verify` also checks every stored anchor against the chain, which detects a rewritten history that was rebuilt into a perfectly valid chain.

**Defaults chosen (confirm or change):**
- Genesis entry rather than re‑chaining existing rows (re‑chaining would rewrite history).
- Anchoring every 60 minutes (`Governance:Anchoring:IntervalMinutes`), first run 30 s after start.
- Any HMAC key used to hash personal or low‑entropy content is held by the petitioner (SIDPA) in its own key store, never by RPAS. RPAS stores only the key's identifier.

## 3. Files Changed

| File | Change |
|------|--------|
| `RPAS.Governance.Core/Models/Governance/GovernanceLedgerEntry.cs` | Chain fields, sealing, immutability guards, server‑assigned timestamp |
| `RPAS.Governance.Core/Models/Governance/LedgerHasher.cs` | Canonical hashing and legacy digest |
| `RPAS.Governance.Persistence/Data/LedgerChain.cs` | Serialized sealing, genesis, contention handling |
| `RPAS.Governance.Persistence/Data/LedgerVerifier.cs` | Chain and anchor verification; `ILedgerAnchorSink` |
| `RPAS.Governance.Persistence/Data/RpasLawEnforcementInterceptor.cs` | Rejects modification of sealed entries and all deletions |
| `RPAS.Governance.Persistence/Migrations/*AddLedgerHashChain*` | Columns, unique index, append‑only triggers |
| `RPAS.Governance.Api/Anchoring/*` | Sinks, service, registration |
| `RPAS.Governance.Api/Controllers/LedgerController.cs` | `/Ledger/head`, `/Ledger/verify` |
| `RPAS.Governance.Api/Controllers/ValidationController.cs` | Chained saves; changes to existing entries recorded as references; GUID comparison for entry lookup |
| `governance-docs/ledger-append-only-grants.sql` | Least‑privilege grants for the application role |

## 4. Verification

- `LedgerChainTests` (SQLite): genesis and links, legacy commitment, 16 parallel writers → one contiguous chain, microsecond timestamp stability, duplicate position rejected, tampering detected (altered content, deleted entry, broken link, hash without sequence), sealed entries reject every mutation, interceptor blocks modify and delete.
- `AnchoringTests`: anchoring writes and records, no churn without new entries, a fully rebuilt but self‑consistent forged history **passes chain verification and is caught by the anchor**, the service refuses to anchor the forged ledger, truncated history is detected, file sink is create‑only.
- `EvidenceApiTests`: verify/head endpoints, tampering returns 409, override of a chained entry leaves the original untouched, anchoring wired through configuration.
- `PostgresTests` (real PostgreSQL 16, real migrations; run with `RPAS_TEST_POSTGRES`): UPDATE/DELETE/TRUNCATE rejected by the triggers, legacy rows frozen once the chain exists, least‑privilege role, two writers claiming one position cannot fork the chain, 24 parallel writers → one chain, timestamp round trip, and the AMD‑2026‑10‑01‑0001 atomic token update (32 contenders, exactly one wins). Migration up/down/up verified.
- **Not exercised:** the Azure Blob sink (compiled, not run against a storage account) and the container's immutability policy, which is what makes anchors tamper‑proof and must be enabled by the operator (time‑based retention on the anchor container).

## 5. Operational Impact

- Apply the migration. The first petition after deployment creates the genesis entry; from then on **no ledger row can be updated or deleted**, including legacy rows. Tooling that edited ledger rows must record a new entry instead.
- Run `ledger-append-only-grants.sql` for the application role. The application role must not own the table or be a superuser (an owner can disable triggers). Run migrations with a separate role.
- Enable anchoring (`Governance:Anchoring:Provider`, plus `ContainerUri` or `Path`) and an immutability policy on the container. Without an external anchor the chain detects accidental and partial tampering, but an attacker with full database write access can rebuild a consistent chain.
- Ledger reads that relied on `IsOverridden`/`Status` of a chained entry must also consult entries that refer to it.

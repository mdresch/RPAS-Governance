# CSR-42: Sovereign Extraction Walkthroughs

This document bundles the verified walkthroughs for the development phases leading to the CSR-42 baseline.

## Phase 2: Law as Code
- **Invariants**: Implementation of `BusinessCase` rules.
- **Enforcement**: `RpasLawEnforcementInterceptor` blocking invalid saves.

## Phase 3: Sovereign Extraction
- **Discovery**: Separation of ADPA and Governance repositories.
- **Persistence**: Migration of `GovernanceDbContext` to the central courthouse.
- **Mutation Gateway**: `ValidationController` becomes the sole entry point for state change.

## Phase 4: Service Mesh & Resilience
- **Infrastructure**: Aspire ServiceDefaults integration.
- **Observability**: OpenTelemetry metrics for rejections.
- **Resilience**: Standard retry backoff (excluding 409s).

## Phase 5: Topology & Enforcement
- **Write-Gates**: AuthorityToken issuance with 120s TTL.
- **Consumption**: Atomic consumption during mutation effects.
- **G6 Envelopes**: `RitualEnvelope` path binding and blocking.

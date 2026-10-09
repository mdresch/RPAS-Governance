/**
 * Scopes for authority tokens defined in RPAS-CM-GRA-001 and AMD-2026-10-01-0007.
 */
export type RitualScope = 'declare-intent' | 'implement' | 'heal' | 'edit-contract' | string;

/**
 * Hash algorithms permitted under HashOnlyPolicy (AMD-2026-10-01-0006).
 */
export type HashAlgorithm = 'SHA-256' | 'SHA-512' | 'HMAC-SHA-256' | 'HMAC-SHA-512';

/**
 * An issued authority token authorizing single-use mutation within declared allowed paths (G6).
 */
export interface AuthorityToken {
  id: string;
  expiresAt: string;
  ritualType: string;
  scope: string;
  allowedPaths: string[];
}

/**
 * Request payload for issuing a scoped authority token.
 */
export interface TokenIssueRequest {
  ritualType: string;
  entityId: string;
}

/**
 * Metadata value types permitted by HashOnlyPolicy: strings, booleans, or non-negative integers.
 */
export type MetadataValue = string | boolean | number;

/**
 * Request payload for recording hash-only evidence on the ledger (AMD-2026-10-01-0006).
 */
export interface EvidenceRecordRequest {
  ritualType: string;
  entityId: string;
  contentHash: string;
  hashAlgorithm: HashAlgorithm | string;
  keyId?: string;
  metadata?: Record<string, MetadataValue>;
}

/**
 * Result returned upon successfully recording evidence on the immutable hash chain.
 */
export interface EvidenceRecordResult {
  id: string;
  sequence: number;
  entryHash: string;
  prevHash: string;
}

/**
 * Request payload for executing a mutation effect using a consumed authority token.
 */
export interface MutationRequest {
  tokenId: string;
  targetPath: string;
}

/**
 * Result of executing a mutation effect through the Courthouse.
 */
export interface MutationResult {
  status: string;
  message: string;
  ritualType: string;
  scope?: string;
  entityId: string;
  path: string;
}

/**
 * Versioned definition of a ritual in force for a petitioner (AMD-2026-10-01-0007).
 */
export interface RitualDefinition {
  id: string;
  version: number;
  petitionerId: string;
  ritualType: string;
  isRetired: boolean;
  acceptsEvidence: boolean;
  metadataKeys: string[];
  scope?: string;
  allowedPaths: string[];
  definitionHash: string;
}

/**
 * Request payload for publishing or superseding a ritual definition (Governor only).
 */
export interface RitualDefinitionPublishRequest {
  petitionerId?: string;
  ritualType: string;
  isRetired?: boolean;
  acceptsEvidence?: boolean;
  metadataKeys?: string[];
  scope?: string;
  allowedPaths?: string[];
}

/**
 * Current head state of the governance ledger hash chain.
 */
export interface LedgerHead {
  sequence: number;
  entryHash: string;
  chainOk: boolean;
}

/**
 * Full cryptographic verification of the ledger hash chain.
 */
export interface LedgerVerifyResult {
  ok: boolean;
  chain: {
    ok: boolean;
    headSequence: number;
    headHash: string;
    genesisHash: string;
    problems?: string[];
  };
  anchors?: unknown;
}

/**
 * Configuration options for RpasGovernanceClient.
 */
export interface ClientOptions {
  /** Base URL of the Courthouse API (e.g. "https://courthouse.rpas.internal" or "http://localhost:5000") */
  baseUrl: string;
  /** Function or string returning the bearer token for petitioner authentication */
  getBearerToken: () => Promise<string> | string;
  /** Timeout in milliseconds for HTTP requests (default: 10,000ms). Fail-closed on timeout. */
  timeoutMs?: number;
  /** Optional custom fetch implementation (defaults to global fetch) */
  fetch?: typeof fetch;
}


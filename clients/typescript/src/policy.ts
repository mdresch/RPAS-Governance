import { HashAlgorithm, MetadataValue } from './types.js';

export const MAX_METADATA_ENTRIES = 16;

export const HASH_ALGORITHM_LENGTHS: Record<string, number> = {
  'SHA-256': 64,
  'SHA-512': 128,
  'HMAC-SHA-256': 64,
  'HMAC-SHA-512': 128,
};

const IDENTIFIER_REGEX = /^[A-Za-z0-9._:/+-]{1,128}$/;
const LOWER_HEX_REGEX = /^[0-9a-f]+$/;

/**
 * Validates whether a given string adheres to RPAS identifier format
 * (1-128 characters: letters, digits, and . _ : / + -).
 */
export function isIdentifier(value?: string | null): boolean {
  return typeof value === 'string' && IDENTIFIER_REGEX.test(value);
}

/**
 * Validates whether a metadata value conforms to HashOnlyPolicy (string identifier, boolean, or non-negative integer).
 */
export function isValidMetadataValue(value: unknown): value is MetadataValue {
  if (typeof value === 'string') {
    return isIdentifier(value);
  }
  if (typeof value === 'boolean') {
    return true;
  }
  if (typeof value === 'number') {
    return Number.isInteger(value) && value >= 0 && value <= 2147483647;
  }
  return false;
}

/**
 * Validates the shape of a hash-only evidence request.
 * Returns null if valid, or a string describing the validation problem.
 */
export function validateEvidenceRequest(params: {
  ritualType: string;
  entityId: string;
  contentHash: string;
  hashAlgorithm: HashAlgorithm | string;
  keyId?: string;
  metadata?: Record<string, unknown>;
  allowedKeys?: readonly string[];
}): string | null {
  const { ritualType, entityId, contentHash, hashAlgorithm, keyId, metadata, allowedKeys } = params;

  if (!isIdentifier(ritualType)) {
    return 'ritualType must be a valid identifier.';
  }

  if (!isIdentifier(entityId)) {
    return 'entityId must be an identifier (letters, digits and . _ : / + - only, 1-128 characters).';
  }

  const expectedLength = HASH_ALGORITHM_LENGTHS[hashAlgorithm];
  if (!expectedLength) {
    return `hashAlgorithm must be one of: ${Object.keys(HASH_ALGORITHM_LENGTHS).join(', ')}.`;
  }

  if (!contentHash || contentHash.length !== expectedLength || !LOWER_HEX_REGEX.test(contentHash)) {
    return `contentHash must be ${expectedLength} lowercase hexadecimal characters for ${hashAlgorithm}.`;
  }

  if (hashAlgorithm.startsWith('HMAC')) {
    if (!isIdentifier(keyId)) {
      return 'keyId (an identifier for the HMAC key, never the key itself) is required for HMAC algorithms.';
    }
  } else if (keyId !== undefined && keyId !== null) {
    return 'keyId is only valid with an HMAC algorithm.';
  }

  if (metadata) {
    const entries = Object.entries(metadata);
    if (entries.length > MAX_METADATA_ENTRIES) {
      return `metadata may contain at most ${MAX_METADATA_ENTRIES} entries.`;
    }

    for (const [key, val] of entries) {
      if (!isIdentifier(key)) {
        return `metadata key must be a valid identifier.`;
      }

      if (allowedKeys && !allowedKeys.includes(key)) {
        return `metadata key '${key}' is not allowed for ritual '${ritualType}'.`;
      }

      if (!isValidMetadataValue(val)) {
        return `metadata value for '${key}' must be an identifier-style string, a boolean or a non-negative integer.`;
      }
    }
  }

  return null;
}


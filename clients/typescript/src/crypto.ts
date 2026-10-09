import { createHash, createHmac } from 'node:crypto';

/**
 * Computes a SHA-256 hash of the content, returning a 64-character lowercase hexadecimal string.
 */
export function computeSha256(content: string | Uint8Array): string {
  return createHash('sha256').update(content).digest('hex').toLowerCase();
}

/**
 * Computes a SHA-512 hash of the content, returning a 128-character lowercase hexadecimal string.
 */
export function computeSha512(content: string | Uint8Array): string {
  return createHash('sha512').update(content).digest('hex').toLowerCase();
}

/**
 * Computes an HMAC-SHA-256 of the content using the specified key, returning a 64-character lowercase hex string.
 */
export function computeHmacSha256(content: string | Uint8Array, key: string | Uint8Array): string {
  return createHmac('sha256', key).update(content).digest('hex').toLowerCase();
}

/**
 * Computes an HMAC-SHA-512 of the content using the specified key, returning a 128-character lowercase hex string.
 */
export function computeHmacSha512(content: string | Uint8Array, key: string | Uint8Array): string {
  return createHmac('sha512', key).update(content).digest('hex').toLowerCase();
}


import { test, describe, mock } from 'node:test';
import assert from 'node:assert/strict';
import {
  RpasGovernanceClient,
  computeSha256,
  computeHmacSha256,
  validateEvidenceRequest,
  GovernanceNetworkError,
  GovernanceValidationError,
  PetitionerAuthenticationError,
  HumanAuthorityRequiredError,
  TopologyViolationError,
  RpasLawViolationError,
} from '../dist/index.js';

describe('HashOnlyPolicy and Crypto Helpers', () => {
  test('computeSha256 produces valid 64-character lowercase hex', () => {
    const hash = computeSha256('hello world');
    assert.equal(hash.length, 64);
    assert.equal(hash, 'b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9');
  });

  test('computeHmacSha256 produces valid HMAC digest', () => {
    const hmac = computeHmacSha256('message', 'secret-key');
    assert.equal(hmac.length, 64);
    assert.match(hmac, /^[0-9a-f]{64}$/);
  });

  test('validateEvidenceRequest accepts well-formed SHA-256 evidence', () => {
    const err = validateEvidenceRequest({
      ritualType: 'IntentDeclared',
      entityId: 'doc-1234',
      contentHash: 'a'.repeat(64),
      hashAlgorithm: 'SHA-256',
      metadata: {
        reviewerRef: 'user_42',
        isPassed: true,
        iterationCount: 3,
      },
    });
    assert.equal(err, null);
  });

  test('validateEvidenceRequest accepts valid HMAC evidence with keyId', () => {
    const err = validateEvidenceRequest({
      ritualType: 'ContractResult',
      entityId: 'contract-99',
      contentHash: 'f'.repeat(64),
      hashAlgorithm: 'HMAC-SHA-256',
      keyId: 'vault-key-01',
      metadata: {
        score: 95,
      },
    });
    assert.equal(err, null);
  });

  test('validateEvidenceRequest rejects HMAC without keyId', () => {
    const err = validateEvidenceRequest({
      ritualType: 'ContractResult',
      entityId: 'contract-99',
      contentHash: 'f'.repeat(64),
      hashAlgorithm: 'HMAC-SHA-256',
    });
    assert.match(err ?? '', /keyId.*required for HMAC/);
  });

  test('validateEvidenceRequest rejects plain hash with keyId', () => {
    const err = validateEvidenceRequest({
      ritualType: 'ContractResult',
      entityId: 'contract-99',
      contentHash: 'f'.repeat(64),
      hashAlgorithm: 'SHA-256',
      keyId: 'unexpected-key',
    });
    assert.match(err ?? '', /keyId is only valid with an HMAC/);
  });

  test('validateEvidenceRequest rejects invalid hash length or non-hex chars', () => {
    const err1 = validateEvidenceRequest({
      ritualType: 'IntentDeclared',
      entityId: 'doc-1',
      contentHash: 'a'.repeat(63), // too short
      hashAlgorithm: 'SHA-256',
    });
    assert.match(err1 ?? '', /must be 64 lowercase hexadecimal characters/);

    const err2 = validateEvidenceRequest({
      ritualType: 'IntentDeclared',
      entityId: 'doc-1',
      contentHash: 'g'.repeat(64), // invalid hex
      hashAlgorithm: 'SHA-256',
    });
    assert.match(err2 ?? '', /must be 64 lowercase hexadecimal characters/);
  });

  test('validateEvidenceRequest rejects illegal metadata values', () => {
    const err = validateEvidenceRequest({
      ritualType: 'IntentDeclared',
      entityId: 'doc-1',
      contentHash: 'a'.repeat(64),
      hashAlgorithm: 'SHA-256',
      metadata: {
        nested: { invalid: 'object' } as any,
      },
    });
    assert.match(err ?? '', /metadata value.*must be an identifier-style string/);
  });
});

describe('RpasGovernanceClient Fail-Closed HTTP Pipeline', () => {
  const fakeToken = 'test-bearer-token';
  const getBearerToken = () => fakeToken;

  test('Client fails closed when network fails or times out', async () => {
    const client = new RpasGovernanceClient({
      baseUrl: 'http://127.0.0.1:9', // unreachable port
      getBearerToken,
      timeoutMs: 500,
    });

    await assert.rejects(
      async () => {
        await client.getLedgerHead();
      },
      (err: unknown) => {
        assert.ok(err instanceof GovernanceNetworkError);
        assert.match((err as Error).message, /failing closed/);
        return true;
      }
    );
  });

  test('issueToken issues token successfully on 201', async () => {
    const mockResponse: Response = {
      ok: true,
      status: 201,
      statusText: 'Created',
      json: async () => ({
        id: '11111111-2222-3333-4444-555555555555',
        expiresAt: '2026-10-08T20:00:00Z',
        ritualType: 'DeclareIntent',
        scope: 'declare-intent',
        allowedPaths: ['/governed/intent/*'],
      }),
    } as unknown as Response;

    const customFetch = mock.fn(async (url: string | URL | Request, init?: RequestInit) => {
      assert.equal(init?.method, 'POST');
      assert.ok(String(url).endsWith('/Tokens/issue'));
      const headers = new Headers(init?.headers);
      assert.equal(headers.get('Authorization'), `Bearer ${fakeToken}`);
      return mockResponse;
    });

    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: customFetch as unknown as typeof fetch,
    });

    const token = await client.issueToken({
      ritualType: 'DeclareIntent',
      entityId: 'intent-001',
    });

    assert.equal(token.scope, 'declare-intent');
    assert.equal(token.id, '11111111-2222-3333-4444-555555555555');
  });

  test('issueToken maps 403 human-only error to HumanAuthorityRequiredError', async () => {
    const mockResponse: Response = {
      ok: false,
      status: 403,
      statusText: 'Forbidden',
      text: async () => JSON.stringify({
        error: "Scope 'edit-contract' is issued to a named human only; an automated petitioner cannot obtain it.",
      }),
    } as unknown as Response;

    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: async () => mockResponse,
    });

    await assert.rejects(
      async () => {
        await client.issueToken({
          ritualType: 'EditContract',
          entityId: 'contract-001',
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof HumanAuthorityRequiredError);
        assert.match((err as Error).message, /Human authority required/);
        return true;
      }
    );
  });

  test('recordEvidence validates client-side and throws GovernanceValidationError without sending request', async () => {
    const fetchMock = mock.fn();
    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: fetchMock as unknown as typeof fetch,
    });

    await assert.rejects(
      async () => {
        await client.recordEvidence({
          ritualType: 'IntentDeclared',
          entityId: 'doc-1',
          contentHash: 'bad-hash',
          hashAlgorithm: 'SHA-256',
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof GovernanceValidationError);
        return true;
      }
    );

    assert.equal(fetchMock.mock.callCount(), 0);
  });

  test('recordEvidence records evidence successfully on 201', async () => {
    const mockResponse: Response = {
      ok: true,
      status: 201,
      statusText: 'Created',
      json: async () => ({
        id: '22222222-3333-4444-5555-666666666666',
        sequence: 42,
        entryHash: 'abc456',
        prevHash: 'def123',
      }),
    } as unknown as Response;

    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: async () => mockResponse,
    });

    const res = await client.recordEvidence({
      ritualType: 'IntentDeclared',
      entityId: 'doc-1',
      contentHash: 'a'.repeat(64),
      hashAlgorithm: 'SHA-256',
      metadata: { reviewerRef: 'reviewer_1' },
    });

    assert.equal(res.sequence, 42);
    assert.equal(res.entryHash, 'abc456');
  });

  test('executeMutation maps 403 Topology violation to TopologyViolationError', async () => {
    const mockResponse: Response = {
      ok: false,
      status: 403,
      statusText: 'Forbidden',
      text: async () => JSON.stringify({
        error: 'Topology Violation (G6)',
        path: '/governed/contracts/secret.ts',
        details: 'Requested path is outside the authorized ritual envelope.',
      }),
    } as unknown as Response;

    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: async () => mockResponse,
    });

    await assert.rejects(
      async () => {
        await client.executeMutation({
          tokenId: '11111111-1111-1111-1111-111111111111',
          targetPath: '/governed/contracts/secret.ts',
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof TopologyViolationError);
        assert.equal((err as TopologyViolationError).targetPath, '/governed/contracts/secret.ts');
        return true;
      }
    );
  });

  test('executeMutation maps 409 conflict to RpasLawViolationError', async () => {
    const mockResponse: Response = {
      ok: false,
      status: 409,
      statusText: 'Conflict',
      text: async () => JSON.stringify({
        error: 'Authority token has already been consumed (Replay detected).',
        rule: 'TokenReplayRule',
      }),
    } as unknown as Response;

    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: async () => mockResponse,
    });

    await assert.rejects(
      async () => {
        await client.executeMutation({
          tokenId: '11111111-1111-1111-1111-111111111111',
          targetPath: '/governed/intent/spec.json',
        });
      },
      (err: unknown) => {
        assert.ok(err instanceof RpasLawViolationError);
        assert.equal((err as RpasLawViolationError).rule, 'TokenReplayRule');
        return true;
      }
    );
  });

  test('verifyLedger returns cryptographic verification report', async () => {
    const mockResponse: Response = {
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        ok: true,
        chain: {
          ok: true,
          headSequence: 10,
          headHash: 'head_hash_val',
          genesisHash: 'genesis_hash_val',
        },
      }),
    } as unknown as Response;

    const client = new RpasGovernanceClient({
      baseUrl: 'http://courthouse.test',
      getBearerToken,
      fetch: async () => mockResponse,
    });

    const result = await client.verifyLedger();
    assert.equal(result.ok, true);
    assert.equal(result.chain.headSequence, 10);
  });
});


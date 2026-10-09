import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import {
  RPAS_CONTRACT_VERSION,
  SIDPA_SCOPED_RITUALS,
  RESERVED_PATH_ENVELOPES,
  SEEDED_EVIDENCE_KEYS,
  validateEvidenceRequest,
} from '../dist/index.js';
import type {
  AuthorityToken,
  EvidenceRecordResult,
  MutationResult,
  RitualDefinition,
  LedgerHead,
  LedgerVerifyResult,
} from '../dist/index.js';

describe('Cross-Repo Contract Specifications (AMD-2026-10-01-0010)', () => {
  test('Contract baseline version is CSR-42', () => {
    assert.equal(RPAS_CONTRACT_VERSION, 'CSR-42');
  });

  test('SIDPA scoped rituals define exact scopes, paths, and human-only rules', () => {
    assert.equal(SIDPA_SCOPED_RITUALS.DECLARE_INTENT.scope, 'declare-intent');
    assert.deepEqual(SIDPA_SCOPED_RITUALS.DECLARE_INTENT.allowedPaths, ['/governed/intent/*']);
    assert.equal(SIDPA_SCOPED_RITUALS.DECLARE_INTENT.isHumanOnly, false);

    assert.equal(SIDPA_SCOPED_RITUALS.IMPLEMENT.scope, 'implement');
    assert.deepEqual(SIDPA_SCOPED_RITUALS.IMPLEMENT.allowedPaths, ['/governed/implementation/*']);
    assert.equal(SIDPA_SCOPED_RITUALS.IMPLEMENT.isHumanOnly, false);

    assert.equal(SIDPA_SCOPED_RITUALS.HEAL.scope, 'heal');
    assert.deepEqual(SIDPA_SCOPED_RITUALS.HEAL.allowedPaths, ['/governed/implementation/*']);
    assert.equal(SIDPA_SCOPED_RITUALS.HEAL.isHumanOnly, false);

    assert.equal(SIDPA_SCOPED_RITUALS.EDIT_CONTRACT.scope, 'edit-contract');
    assert.deepEqual(SIDPA_SCOPED_RITUALS.EDIT_CONTRACT.allowedPaths, ['/governed/contracts/*']);
    assert.equal(SIDPA_SCOPED_RITUALS.EDIT_CONTRACT.isHumanOnly, true);
  });

  test('Reserved path envelopes are defined on segment boundaries', () => {
    assert.equal(RESERVED_PATH_ENVELOPES.INTENT, '/governed/intent');
    assert.equal(RESERVED_PATH_ENVELOPES.CONTRACTS, '/governed/contracts');
  });

  test('Seeded evidence rituals have exact metadata key allow-lists', () => {
    assert.deepEqual(SEEDED_EVIDENCE_KEYS.IntentDeclared, ['moduleId', 'intentVersion', 'standardId']);
    assert.deepEqual(SEEDED_EVIDENCE_KEYS.ContractResult, ['moduleId', 'contractSuite', 'contractVersion', 'outcome', 'attempt']);
    assert.deepEqual(SEEDED_EVIDENCE_KEYS.HealAttempt, ['moduleId', 'attempt', 'outcome', 'scope']);
    assert.deepEqual(SEEDED_EVIDENCE_KEYS.EvidenceRecorded, ['documentId', 'documentVersion', 'standardId', 'ruleSetId', 'ruleSetVersion']);
    assert.deepEqual(SEEDED_EVIDENCE_KEYS.ScoreAttested, ['documentId', 'documentVersion', 'standardId', 'ruleSetId', 'ruleSetVersion', 'scoreBand', 'scoringMethodVersion']);
    assert.deepEqual(SEEDED_EVIDENCE_KEYS.HumanAttestation, ['documentId', 'documentVersion', 'reviewerRef', 'decision', 'scoreBand', 'overrideApplied']);
  });

  test('validateEvidenceRequest enforces contract metadata keys', () => {
    const valid = validateEvidenceRequest({
      ritualType: 'IntentDeclared',
      entityId: 'intent-1',
      contentHash: '0'.repeat(64),
      hashAlgorithm: 'SHA-256',
      metadata: {
        moduleId: 'core-module',
        intentVersion: 'v1.0.0',
        standardId: 'ISO-27001',
      },
      allowedKeys: SEEDED_EVIDENCE_KEYS.IntentDeclared,
    });
    assert.equal(valid, null);

    const invalidKey = validateEvidenceRequest({
      ritualType: 'IntentDeclared',
      entityId: 'intent-1',
      contentHash: '0'.repeat(64),
      hashAlgorithm: 'SHA-256',
      metadata: {
        unauthorizedField: 'unauthorized',
      },
      allowedKeys: SEEDED_EVIDENCE_KEYS.IntentDeclared,
    });
    assert.match(invalidKey ?? '', /metadata key 'unauthorizedField' is not allowed/);
  });

  test('Contract DTO types typecheck and instantiate correctly', () => {
    const token: AuthorityToken = {
      id: '00000000-0000-0000-0000-000000000001',
      expiresAt: '2026-10-09T00:00:00Z',
      ritualType: 'DeclareIntent',
      scope: 'declare-intent',
      allowedPaths: ['/governed/intent/*'],
    };
    assert.equal(token.scope, 'declare-intent');

    const evidenceResult: EvidenceRecordResult = {
      id: '00000000-0000-0000-0000-000000000002',
      sequence: 1,
      entryHash: 'a'.repeat(64),
      prevHash: '0'.repeat(64),
    };
    assert.equal(evidenceResult.sequence, 1);

    const mutationResult: MutationResult = {
      status: 'executed',
      message: 'OK',
      ritualType: 'DeclareIntent',
      entityId: 'intent-1',
      path: '/governed/intent/manifest.json',
    };
    assert.equal(mutationResult.status, 'executed');

    const definition: RitualDefinition = {
      id: '00000000-0000-0000-0000-000000000003',
      version: 1,
      petitionerId: '*',
      ritualType: 'DeclareIntent',
      isRetired: false,
      acceptsEvidence: false,
      metadataKeys: [],
      scope: 'declare-intent',
      allowedPaths: ['/governed/intent/*'],
      definitionHash: 'b'.repeat(64),
    };
    assert.equal(definition.version, 1);

    const head: LedgerHead = {
      sequence: 5,
      entryHash: 'c'.repeat(64),
      chainOk: true,
    };
    assert.equal(head.chainOk, true);

    const verify: LedgerVerifyResult = {
      ok: true,
      chain: {
        ok: true,
        headSequence: 5,
        headHash: 'c'.repeat(64),
        genesisHash: '0'.repeat(64),
      },
    };
    assert.equal(verify.ok, true);
  });
});

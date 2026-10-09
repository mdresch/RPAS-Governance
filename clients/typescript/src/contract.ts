/**
 * RPAS Sovereign Governance Courthouse Contract Specifications (CSR-42 / AMD-2026-10-01-0007 / AMD-0010).
 * Defines authoritative ritual names, scopes, reserved envelopes, and evidence keys.
 */

export const RPAS_CONTRACT_VERSION = 'CSR-42';

export const SIDPA_SCOPED_RITUALS = {
  DECLARE_INTENT: {
    ritualType: 'DeclareIntent',
    scope: 'declare-intent',
    allowedPaths: ['/governed/intent/*'],
    isHumanOnly: false,
  },
  IMPLEMENT: {
    ritualType: 'Implement',
    scope: 'implement',
    allowedPaths: ['/governed/implementation/*'],
    isHumanOnly: false,
  },
  HEAL: {
    ritualType: 'Heal',
    scope: 'heal',
    allowedPaths: ['/governed/implementation/*'],
    isHumanOnly: false,
  },
  EDIT_CONTRACT: {
    ritualType: 'EditContract',
    scope: 'edit-contract',
    allowedPaths: ['/governed/contracts/*'],
    isHumanOnly: true,
  },
} as const;

export const RESERVED_PATH_ENVELOPES = {
  INTENT: '/governed/intent',
  CONTRACTS: '/governed/contracts',
} as const;

export const SEEDED_EVIDENCE_KEYS: Record<string, readonly string[]> = {
  IntentDeclared: ['moduleId', 'intentVersion', 'standardId'],
  ContractResult: ['moduleId', 'contractSuite', 'contractVersion', 'outcome', 'attempt'],
  HealAttempt: ['moduleId', 'attempt', 'outcome', 'scope'],
  EvidenceRecorded: ['documentId', 'documentVersion', 'standardId', 'ruleSetId', 'ruleSetVersion'],
  ScoreAttested: ['documentId', 'documentVersion', 'standardId', 'ruleSetId', 'ruleSetVersion', 'scoreBand', 'scoringMethodVersion'],
  HumanAttestation: ['documentId', 'documentVersion', 'reviewerRef', 'decision', 'scoreBand', 'overrideApplied'],
} as const;


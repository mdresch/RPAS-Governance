/**
 * Base error for all RPAS Sovereign Governance Courthouse client operations.
 */
export class GovernanceError extends Error {
  public readonly statusCode?: number;
  public readonly details?: unknown;

  constructor(message: string, statusCode?: number, details?: unknown) {
    super(message);
    this.name = 'GovernanceError';
    this.statusCode = statusCode;
    this.details = details;
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

/**
 * Thrown when the Courthouse cannot be reached due to connection failure, DNS resolution error,
 * or request timeout. Under RPAS fail-closed semantics, no unverified mutation is ever permitted.
 */
export class GovernanceNetworkError extends GovernanceError {
  constructor(message: string, cause?: unknown) {
    super(`RPAS Courthouse unreachable (failing closed): ${message}`, undefined, cause);
    this.name = 'GovernanceNetworkError';
  }
}

/**
 * Thrown on HTTP 409 Conflict: structural law violation, token replay/already consumed,
 * TTL expired, or ledger contention.
 */
export class RpasLawViolationError extends GovernanceError {
  public readonly rule?: string;

  constructor(message: string, rule?: string, details?: unknown) {
    super(message, 409, details);
    this.name = 'RpasLawViolationError';
    this.rule = rule;
  }
}

/**
 * Thrown on HTTP 403 Forbidden when a requested mutation path violates the declared
 * authority token path envelope (G6 Topology Boundary).
 */
export class TopologyViolationError extends GovernanceError {
  public readonly targetPath: string;

  constructor(targetPath: string, message: string, details?: unknown) {
    super(`Topology Violation (G6): Target path '${targetPath}' is outside the authorized envelope. ${message}`, 403, details);
    this.name = 'TopologyViolationError';
    this.targetPath = targetPath;
  }
}

/**
 * Thrown on HTTP 401 or 403 when petitioner authentication is missing, invalid,
 * or the petitioner has not been granted the required scope.
 */
export class PetitionerAuthenticationError extends GovernanceError {
  constructor(message: string, statusCode: number = 401, details?: unknown) {
    super(message, statusCode, details);
    this.name = 'PetitionerAuthenticationError';
  }
}

/**
 * Thrown on HTTP 403 when an operation requires a named human (delegated Entra user token),
 * but was invoked with an automated or app-only token (e.g. edit-contract scope or definition publishing).
 */
export class HumanAuthorityRequiredError extends GovernanceError {
  constructor(message: string, details?: unknown) {
    super(`Human authority required: ${message}`, 403, details);
    this.name = 'HumanAuthorityRequiredError';
  }
}

/**
 * Thrown on HTTP 400 Bad Request when request parameters violate HashOnlyPolicy
 * or parameter constraints.
 */
export class GovernanceValidationError extends GovernanceError {
  constructor(message: string, details?: unknown) {
    super(message, 400, details);
    this.name = 'GovernanceValidationError';
  }
}


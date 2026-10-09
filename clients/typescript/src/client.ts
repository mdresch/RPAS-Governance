import {
  ClientOptions,
  AuthorityToken,
  TokenIssueRequest,
  EvidenceRecordRequest,
  EvidenceRecordResult,
  MutationRequest,
  MutationResult,
  RitualDefinition,
  RitualDefinitionPublishRequest,
  LedgerHead,
  LedgerVerifyResult,
  LedgerReplayReport,
} from './types.js';
import {
  GovernanceError,
  GovernanceNetworkError,
  GovernanceValidationError,
  PetitionerAuthenticationError,
  TopologyViolationError,
  HumanAuthorityRequiredError,
  RpasLawViolationError,
} from './errors.js';
import { validateEvidenceRequest } from './policy.js';

export class RpasGovernanceClient {
  private readonly baseUrl: string;
  private readonly getBearerToken: () => Promise<string> | string;
  private readonly timeoutMs: number;
  private readonly fetchImpl: typeof fetch;

  constructor(options: ClientOptions) {
    if (!options.baseUrl) {
      throw new GovernanceValidationError('baseUrl is required.');
    }
    this.baseUrl = options.baseUrl.replace(/\/+$/, '');
    this.getBearerToken = options.getBearerToken;
    this.timeoutMs = options.timeoutMs ?? 10_000;
    this.fetchImpl = options.fetch ?? globalThis.fetch;
  }

  /**
   * Issues a scoped authority token for a ritual (AMD-2026-10-01-0007).
   * Scopes: declare-intent, implement, heal, edit-contract.
   * edit-contract requires a human-attributed delegated token.
   */
  public async issueToken(request: TokenIssueRequest): Promise<AuthorityToken> {
    if (!request.ritualType || !request.entityId) {
      throw new GovernanceValidationError('ritualType and entityId are required.');
    }

    return this.post<AuthorityToken>('/Tokens/issue', request);
  }

  /**
   * Records a hash-only evidence proof on the Sovereign Ledger (AMD-2026-10-01-0006).
   * Validates parameter constraints fail-fast before transmission.
   */
  public async recordEvidence(request: EvidenceRecordRequest): Promise<EvidenceRecordResult> {
    const validationProblem = validateEvidenceRequest(request);
    if (validationProblem) {
      throw new GovernanceValidationError(validationProblem);
    }

    return this.post<EvidenceRecordResult>('/Evidence/record', request);
  }

  /**
   * Executes a mutation effect using an unconsumed authority token (G6 Topology enforcement).
   */
  public async executeMutation(request: MutationRequest): Promise<MutationResult> {
    if (!request.tokenId || !request.targetPath) {
      throw new GovernanceValidationError('tokenId and targetPath are required.');
    }

    return this.post<MutationResult>('/Mutation/execute', request);
  }

  /**
   * Lists active ritual definitions in force for the calling petitioner.
   */
  public async listRitualDefinitions(): Promise<RitualDefinition[]> {
    return this.get<RitualDefinition[]>('/Rituals/definitions');
  }

  /**
   * Publishes or supersedes a versioned ritual definition (requires Governor human token).
   */
  public async publishRitualDefinition(request: RitualDefinitionPublishRequest): Promise<RitualDefinition> {
    if (!request.ritualType) {
      throw new GovernanceValidationError('ritualType is required.');
    }

    return this.post<RitualDefinition>('/Rituals/definitions', request);
  }

  /**
   * Retrieves the current head sequence and hash of the Sovereign Ledger.
   */
  public async getLedgerHead(): Promise<LedgerHead> {
    return this.get<LedgerHead>('/Ledger/head');
  }

  /**
   * Verifies the cryptographic integrity of the entire ledger hash chain and external anchors.
   */
  public async verifyLedger(): Promise<LedgerVerifyResult> {
    return this.get<LedgerVerifyResult>('/Ledger/verify');
  }

  /**
   * Replays the entire ledger event stream from sequence 1 to head, reconstituting
   * state counts and verifying cryptographic continuity (AMD-2026-10-01-0011).
   */
  public async replayLedger(): Promise<LedgerReplayReport> {
    return this.get<LedgerReplayReport>('/Ledger/replay');
  }

  // --- Internal HTTP Pipeline with Fail-Closed Error Mapping ---

  private async get<T>(path: string): Promise<T> {
    return this.send<T>(path, { method: 'GET' });
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    return this.send<T>(path, {
      method: 'POST',
      body: JSON.stringify(body),
    });
  }

  private async send<T>(path: string, init: RequestInit): Promise<T> {
    const url = `${this.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
    let token: string;

    try {
      token = await this.getBearerToken();
    } catch (err) {
      throw new PetitionerAuthenticationError(`Failed to retrieve bearer token: ${String(err)}`, 401, err);
    }

    const headers = new Headers(init.headers);
    headers.set('Authorization', `Bearer ${token}`);
    headers.set('Accept', 'application/json');
    if (init.body) {
      headers.set('Content-Type', 'application/json');
    }

    let controller: AbortController | undefined;
    let timeoutId: NodeJS.Timeout | undefined;

    if (typeof AbortController !== 'undefined') {
      controller = new AbortController();
      timeoutId = setTimeout(() => controller?.abort(), this.timeoutMs);
    }

    let response: Response;
    try {
      response = await this.fetchImpl(url, {
        ...init,
        headers,
        signal: controller?.signal,
      });
    } catch (error: unknown) {
      const isAbort = (error as { name?: string })?.name === 'AbortError';
      const msg = isAbort ? `Request timed out after ${this.timeoutMs}ms` : String(error);
      throw new GovernanceNetworkError(msg, error);
    } finally {
      if (timeoutId) {
        clearTimeout(timeoutId);
      }
    }

    if (!response.ok) {
      await this.handleErrorResponse(response, init);
    }

    try {
      return (await response.json()) as T;
    } catch (parseError) {
      throw new GovernanceError(
        `Failed to parse response JSON from Courthouse: ${String(parseError)}`,
        response.status,
        parseError
      );
    }
  }

  private async handleErrorResponse(response: Response, init: RequestInit): Promise<never> {
    let errorBody: Record<string, unknown> | null = null;
    let message = `Courthouse returned HTTP ${response.status} ${response.statusText}`;

    try {
      const text = await response.text();
      try {
        errorBody = JSON.parse(text);
        if (typeof errorBody?.error === 'string') {
          message = errorBody.error;
        } else if (typeof errorBody?.message === 'string') {
          message = errorBody.message;
        }
      } catch {
        if (text) {
          message = text;
        }
      }
    } catch {
      // response body could not be read
    }

    // 400 Bad Request
    if (response.status === 400) {
      throw new GovernanceValidationError(message, errorBody);
    }

    // 401 Unauthorized
    if (response.status === 401) {
      throw new PetitionerAuthenticationError(message, 401, errorBody);
    }

    // 403 Forbidden
    if (response.status === 403) {
      const path = (errorBody?.path as string) || (typeof init.body === 'string' && JSON.parse(init.body)?.targetPath) || '';
      if (message.toLowerCase().includes('topology') || errorBody?.path) {
        throw new TopologyViolationError(path, message, errorBody);
      }
      if (message.toLowerCase().includes('human') || message.toLowerCase().includes('governor')) {
        throw new HumanAuthorityRequiredError(message, errorBody);
      }
      throw new PetitionerAuthenticationError(message, 403, errorBody);
    }

    // 409 Conflict
    if (response.status === 409) {
      const rule = (errorBody?.rule as string) || undefined;
      throw new RpasLawViolationError(message, rule, errorBody);
    }

    // 503 Service Unavailable (Ledger contention)
    if (response.status === 503) {
      throw new RpasLawViolationError(`Ledger contention: ${message}`, 'LedgerContention', errorBody);
    }

    // General failure
    throw new GovernanceError(message, response.status, errorBody);
  }
}


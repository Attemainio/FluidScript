import type {
  CompileResponse,
  FormatResponse,
  Metadata,
  ProblemDetails,
  ValidateResponse,
} from './types.ts';

/** The body of `POST /api/v1/compile` and `/solve` (`42`). */
export interface CompileRequest {
  readonly sessionId: string;
  readonly script: string;
  readonly solve?: boolean;
  /** The case to draw, for a file that declares cases (`D-182`); absent draws the operating case. */
  readonly case?: string;
}

/** A non-200 answer: the status and the problem details the host sent (`42` error cases). */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** Whether the host abandoned this request because a newer one on the session overtook it (`41`). */
  get superseded(): boolean {
    return this.status === 499;
  }
}

/**
 * The contract major this build reads (`26`). A minor is additive and read by any build of the same
 * major; a major removes a field, a value or a unit, so a body from another major is refused rather
 * than rendered with the wrong meaning.
 */
export const contractMajor = 4;

/**
 * A successful answer written in a contract major this build cannot read (`26`, `51` error cases):
 * the host was upgraded under an open page. Nothing it carries is applied; the user reloads.
 */
export class ContractMismatchError extends ApiError {
  /** The `contractVersion` the host sent. */
  readonly received: string;

  constructor(received: string) {
    super(200, {
      status: 200,
      title: `The host sends model contract ${received}; this page reads ${contractMajor}.x`,
    });
    this.name = 'ContractMismatchError';
    this.received = received;
  }
}

/** The REST calls the editor makes (`42`), typed. Every call takes a signal so the pipeline can abort it. */
export interface ApiClient {
  compile(request: CompileRequest, signal: AbortSignal): Promise<CompileResponse>;
  solve(request: CompileRequest, signal: AbortSignal): Promise<CompileResponse>;
  validate(script: string, signal: AbortSignal): Promise<ValidateResponse>;
  format(script: string, signal: AbortSignal): Promise<FormatResponse>;
  metadata(signal: AbortSignal): Promise<Metadata>;
}

/**
 * A client over `fetch`. `base` is empty in development, where Vite proxies `/api` to the host
 * (`41`), so no CORS configuration exists anywhere.
 */
export function createClient(fetchImpl: typeof fetch = fetch, base = ''): ApiClient {
  const post = async <T>(path: string, body: unknown, signal: AbortSignal): Promise<T> => {
    const response = await fetchImpl(base + path, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
      signal,
    });
    return read<T>(response);
  };

  return {
    compile: (request, signal) => post('/api/v1/compile', request, signal),
    solve: (request, signal) => post('/api/v1/solve', request, signal),
    validate: (script, signal) => post('/api/v1/validate', { script }, signal),
    format: (script, signal) => post('/api/v1/format', { script }, signal),
    metadata: async (signal) =>
      read<Metadata>(await fetchImpl(base + '/api/v1/metadata', { signal })),
  };
}

async function read<T>(response: Response): Promise<T> {
  if (response.ok) {
    const body = (await response.json()) as unknown;
    const version =
      typeof body === 'object' && body !== null && 'contractVersion' in body
        ? (body as { contractVersion: unknown }).contractVersion
        : undefined;
    // Every body the host versions is checked here, once, so no caller can render one unchecked.
    if (typeof version === 'string' && Number.parseInt(version, 10) !== contractMajor) {
      throw new ContractMismatchError(version);
    }
    return body as T;
  }

  let problem: ProblemDetails = { status: response.status, title: response.statusText };
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // Not a problem-details body; the status is all there is.
  }
  throw new ApiError(response.status, problem);
}

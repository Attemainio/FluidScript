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
    return (await response.json()) as T;
  }

  let problem: ProblemDetails = { status: response.status, title: response.statusText };
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // Not a problem-details body; the status is all there is.
  }
  throw new ApiError(response.status, problem);
}

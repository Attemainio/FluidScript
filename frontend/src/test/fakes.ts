import type { ApiClient, CompileRequest } from '../api/client.ts';
import type {
  CompileResponse,
  Diagnostic,
  FormatResponse,
  ModelContract,
  ValidateResponse,
} from '../api/types.ts';
import type { Clock } from '../features/pipeline/compilePipeline.ts';

/** A clock a test advances by hand, so ten seconds of typing take no time at all. */
export class FakeClock implements Clock {
  private time = 0;
  private readonly timers: { at: number; callback: () => void; id: number }[] = [];
  private nextId = 1;

  readonly now = (): number => this.time;
  readonly setTimeout = (callback: () => void, ms: number): unknown => {
    const id = this.nextId++;
    this.timers.push({ at: this.time + ms, callback, id });
    return id;
  };
  readonly clearTimeout = (handle: unknown): void => {
    const index = this.timers.findIndex((t) => t.id === handle);
    if (index >= 0) {
      this.timers.splice(index, 1);
    }
  };

  /** Advances time, firing timers in order, and lets promise callbacks run between them. */
  async advance(ms: number): Promise<void> {
    const end = this.time + ms;
    for (;;) {
      const next = this.timers.filter((t) => t.at <= end).sort((a, b) => a.at - b.at)[0];
      if (next === undefined) {
        break;
      }
      this.timers.splice(this.timers.indexOf(next), 1);
      this.time = next.at;
      next.callback();
      await settle();
    }
    this.time = end;
    await settle();
  }
}

export async function settle(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

/** A compile answer that a test resolves when it chooses. */
export interface Call {
  readonly request: CompileRequest;
  readonly signal: AbortSignal;
  readonly resolve: (response: CompileResponse) => void;
  readonly reject: (error: unknown) => void;
  done?: boolean;
}

export class FakeClient implements ApiClient {
  readonly calls: Call[] = [];
  readonly validations: { script: string; resolve: (r: ValidateResponse) => void }[] = [];

  compile(request: CompileRequest, signal: AbortSignal): Promise<CompileResponse> {
    return new Promise((resolve, reject) => {
      this.calls.push({ request, signal, resolve, reject });
      signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
    });
  }
  solve(request: CompileRequest, signal: AbortSignal): Promise<CompileResponse> {
    return this.compile(request, signal);
  }
  validate(script: string, _signal: AbortSignal): Promise<ValidateResponse> {
    return new Promise((resolve) => this.validations.push({ script, resolve }));
  }
  format(script: string, _signal: AbortSignal): Promise<FormatResponse> {
    return Promise.resolve({ edits: this.formatEdits(script) });
  }
  /** What `format` answers; a test replaces it. */
  formatEdits: (script: string) => FormatResponse['edits'] = () => [];
  metadata(): Promise<never> {
    return Promise.reject(new Error('not used'));
  }

  get inFlight(): Call[] {
    return this.calls.filter((c) => !c.signal.aborted && !c.done);
  }
}

export function model(name: string): ModelContract {
  return {
    contractVersion: '2.0',
    circuits: [{ name }],
    components: [],
    symbols: [],
    connections: [],
    style: { tokens: [], spacing: null, default: { corner: null, pattern: 'solid' }, named: {} },
    layout: {
      placements: [],
      routes: [],
      margin: 0.5,
      extent: [0, 0, 0, 0],
      flow: {},
      inferred: [],
    },
    diagnostics: [],
  } as unknown as ModelContract;
}

export function answer(name: string, solveMs = 10): CompileResponse {
  return {
    model: { ...model(name), solve: { converged: true, iterations: 3 } } as ModelContract,
    timings: { parseMs: 1, bindMs: 1, sizeMs: 1, solveMs, totalMs: solveMs + 3 },
  };
}

export function diagnostic(code: string): Diagnostic {
  return {
    code,
    severity: 'error',
    message: code,
    range: null,
    component: null,
    suggestion: null,
    related: [],
  } as unknown as Diagnostic;
}

export function finish(call: Call, response: CompileResponse): void {
  call.done = true;
  call.resolve(response);
}

import type { Diagnostic, Solve } from '../../api/types.ts';

/** `56`'s three filter states. */
export type LogFilter = 'all' | 'warnings' | 'errors';

export type LogSeverity = 'error' | 'warning' | 'info' | 'ok';

/** One line of the log: a diagnostic, a group of them, or the success line. */
export interface LogEntry {
  /** Stable across compiles: `code` + `component`, or the group's code, so an unchanged entry keeps its element (`56` invariant 2). */
  readonly key: string;
  readonly severity: LogSeverity;
  readonly code: string | null;
  /** The component column; a group says how many. */
  readonly subject: string;
  /** The component to select on click, when the entry is about one. */
  readonly component: string | null;
  readonly message: string;
  /** A group's members, each with its own component and formatted message (`56` grouping). */
  readonly members: readonly { component: string | null; message: string }[] | null;
}

/** `56`'s grouping threshold: three of one code and severity collapse into one line. */
export const groupThreshold = 3;

/** The severity a wire diagnostic maps to; anything unknown is shown as info rather than hidden. */
function severityOf(diagnostic: Diagnostic): LogSeverity {
  return diagnostic.severity === 'error' || diagnostic.severity === 'warning'
    ? diagnostic.severity
    : 'info';
}

/**
 * Turns the current diagnostics into log lines: grouped at `groupThreshold`, keyed for
 * reconciliation, errors first, then warnings, then infos, each in wire order; the success line
 * closes a clean solve (`56` rule 5) and the failure line a solve that did not converge.
 */
export function logEntries(
  diagnostics: readonly Diagnostic[],
  solve: Solve | null | undefined,
  timings: { readonly totalMs: number } | null,
): LogEntry[] {
  const byCode = new Map<string, Diagnostic[]>();
  for (const d of diagnostics) {
    const bucket = `${severityOf(d)}:${d.code}`;
    byCode.set(bucket, [...(byCode.get(bucket) ?? []), d]);
  }

  const entries: LogEntry[] = [];
  const seen = new Set<string>();
  for (const d of diagnostics) {
    const bucket = `${severityOf(d)}:${d.code}`;
    const members = byCode.get(bucket) ?? [];
    if (members.length >= groupThreshold) {
      if (seen.has(bucket)) {
        continue;
      }
      seen.add(bucket);
      const subjects = members.filter((m) => m.component !== null).length;
      entries.push({
        key: `group:${bucket}`,
        severity: severityOf(d),
        code: d.code,
        subject:
          subjects > 0
            ? `${members.length} ${subjects === members.length ? 'components' : 'places'}`
            : `${members.length}×`,
        component: null,
        message: commonMessage(members),
        members: members.map((m) => ({ component: m.component, message: m.message })),
      });
      continue;
    }
    entries.push({
      key: `${d.code}:${d.component ?? ''}:${d.range?.offset ?? ''}`,
      severity: severityOf(d),
      code: d.code,
      subject: d.component ?? '',
      component: d.component,
      message: d.message,
      members: null,
    });
  }

  const rank: Record<LogSeverity, number> = { error: 0, warning: 1, info: 2, ok: 3 };
  entries.sort((a, b) => rank[a.severity] - rank[b.severity]);

  if (solve !== null && solve !== undefined) {
    const ms = timings === null ? '' : ` · ${timings.totalMs} ms`;
    entries.push(
      solve.converged
        ? {
            key: 'ok',
            severity: 'ok',
            code: null,
            subject: '',
            component: null,
            message: `Solved · ${solve.iterations} ${solve.iterations === 1 ? 'iteration' : 'iterations'}${ms}`,
            members: null,
          }
        : {
            key: 'failed',
            severity: 'error',
            code: null,
            subject: '',
            component: null,
            message: `Did not converge after ${solve.iterations} ${solve.iterations === 1 ? 'iteration' : 'iterations'}${ms}`,
            members: null,
          },
    );
  }

  return entries;
}

/** The part of the members' messages they share, for the group's line; the first message when they share nothing useful. */
function commonMessage(members: readonly Diagnostic[]): string {
  const first = members[0]?.message ?? '';
  const words = first.split(' ');
  let common = words.length;
  for (const m of members.slice(1)) {
    const other = m.message.split(' ');
    let k = 0;
    while (k < common && k < other.length && words[k] === other[k]) {
      k++;
    }
    common = k;
  }
  const prefix = words
    .slice(0, common)
    .join(' ')
    .replace(/[\s:,'"-]+$/, '');
  return prefix.length >= 12 ? `${prefix}.` : first;
}

/** Applies `56`'s filter: `warnings` is warnings and errors, the default. The success and failure lines always show. */
export function visibleEntries(
  entries: readonly LogEntry[],
  filter: LogFilter,
  text: string,
): LogEntry[] {
  const needle = text.trim().toLowerCase();
  return entries.filter((e) => {
    if (e.severity === 'ok' || (e.severity === 'error' && e.code === null)) {
      return true;
    }
    if (filter === 'errors' && e.severity !== 'error') {
      return false;
    }
    if (filter === 'warnings' && e.severity === 'info') {
      return false;
    }
    if (needle.length === 0) {
      return true;
    }
    return (
      e.message.toLowerCase().includes(needle) ||
      e.subject.toLowerCase().includes(needle) ||
      (e.code?.toLowerCase().includes(needle) ?? false) ||
      (e.members?.some(
        (m) =>
          m.message.toLowerCase().includes(needle) ||
          (m.component?.toLowerCase().includes(needle) ?? false),
      ) ??
        false)
    );
  });
}

/** The counts the header shows: infos hidden by the default filter, errors for the header's state. */
export function countBySeverity(entries: readonly LogEntry[]): Record<LogSeverity, number> {
  const counts: Record<LogSeverity, number> = { error: 0, warning: 0, info: 0, ok: 0 };
  for (const e of entries) {
    if (e.code === null) {
      continue;
    }
    counts[e.severity] += e.members?.length ?? 1;
  }
  return counts;
}

/** `56`'s copy-as-text: code, severity, component and message per line, the group's members each on their own. */
export function logAsText(entries: readonly LogEntry[]): string {
  const lines: string[] = [];
  for (const e of entries) {
    if (e.members !== null) {
      for (const m of e.members) {
        lines.push([e.code ?? '', e.severity, m.component ?? '', m.message].join('\t'));
      }
    } else {
      lines.push([e.code ?? '', e.severity, e.component ?? '', e.message].join('\t'));
    }
  }
  return lines.join('\n');
}

/**
 * The header's right side (`56`'s table): what the solver did, in one phrase, from the draft's
 * state rather than from the timing alone, so a plant that did not converge never reads as solved.
 */
export function headerStatus(input: {
  readonly compiling: boolean;
  readonly offline: boolean;
  readonly errors: number;
  readonly solve: Solve | null | undefined;
  readonly totalMs: number | null;
}): { readonly text: string; readonly severity: LogSeverity | 'neutral' } {
  if (input.offline) {
    return { text: 'offline', severity: 'neutral' };
  }
  if (input.compiling) {
    return { text: 'solving…', severity: 'neutral' };
  }
  if (input.errors > 0) {
    return {
      text:
        input.solve?.converged === true
          ? `${input.errors} ${input.errors === 1 ? 'error' : 'errors'}`
          : `not solved — ${input.errors} ${input.errors === 1 ? 'error' : 'errors'}`,
      severity: 'error',
    };
  }
  if (input.solve === null || input.solve === undefined) {
    return { text: 'not solved', severity: 'neutral' };
  }
  if (!input.solve.converged) {
    return { text: 'did not converge', severity: 'error' };
  }
  return {
    text: input.totalMs === null ? 'solved' : `solved in ${input.totalMs} ms`,
    severity: 'ok',
  };
}

import { describe, expect, it } from 'vitest';

import type { Diagnostic } from '../../api/types.ts';
import {
  countBySeverity,
  headerStatus,
  logAsText,
  logEntries,
  visibleEntries,
} from './logModel.ts';

function d(code: string, severity: string, component: string | null, message: string): Diagnostic {
  return { code, severity, message, range: null, component, suggestion: null, related: [] };
}

// The brief's example (01, 56's worked example): one FS1507, two FS2107, six FS1510.
const brief: Diagnostic[] = [
  d('FS1510', 'info', null, "Added node 'N1' (I1)."),
  d('FS1510', 'info', null, "Added node 'N2' (I1)."),
  d('FS1510', 'info', null, "Added node 'N3' (I1)."),
  d('FS1510', 'info', null, "Added node 'PU1__HE1' (I2)."),
  d('FS1510', 'info', null, "Added node 'PU1__in' (I3)."),
  d('FS1510', 'info', null, "Added node 'PU1__out' (I3)."),
  d('FS1507', 'error', 'PU1', "'PU1' is not connected to anything."),
  d('FS2107', 'warning', 'N1', "'N1' is a dead end. Set t, p or flow to make it a boundary."),
  d('FS2107', 'warning', 'N3', "'N3' is a dead end. Set t, p or flow to make it a boundary."),
];

describe('the log model', () => {
  it("shows the brief's example as exactly two lines under the default filter, with six info hidden", () => {
    const entries = logEntries(brief, null, null);
    const visible = visibleEntries(entries, 'warnings', '');
    expect(visible.map((e) => `${e.severity} ${e.subject}`)).toEqual([
      'error PU1',
      'warning N1',
      'warning N3',
    ]);
    // Two same-code occurrences stay separate (56: three groups).
    expect(visible.every((e) => e.members === null)).toBe(true);
    expect(countBySeverity(entries).info).toBe(6);
  });

  it('groups three or more of one code and severity into one expandable line', () => {
    const forty = Array.from({ length: 40 }, (_, i) =>
      d('FS4001', 'warning', `N${i + 1}`, `Approaching freezing point: ${2 + i / 10} °C.`),
    );
    const entries = logEntries([...forty, d('FS1507', 'error', 'PU1', 'x')], null, null);
    const group = entries.find((e) => e.members !== null)!;
    expect(entries).toHaveLength(2);
    expect(group.subject).toBe('40 components');
    expect(group.members).toHaveLength(40);
    expect(group.members![3]).toEqual({
      component: 'N4',
      message: 'Approaching freezing point: 2.3 °C.',
    });
    expect(group.message).toBe('Approaching freezing point.');
    expect(group.key).toBe('group:warning:FS4001');
  });

  it('keys an entry by code and component so it survives a recompile unchanged', () => {
    const a = logEntries(brief, null, null);
    const b = logEntries([...brief].reverse(), null, null);
    expect(new Set(a.map((e) => e.key))).toEqual(new Set(b.map((e) => e.key)));
  });

  it('ends a clean solve with the success line and a divergence with the failure line (56 rule 5)', () => {
    const ok = logEntries(
      [],
      { converged: true, iterations: 4, residualNorm: 0, elapsedMs: 14, sizingPasses: 1 },
      { totalMs: 14 },
    );
    expect(ok.map((e) => e.message)).toEqual(['Solved · 4 iterations · 14 ms']);
    expect(visibleEntries(ok, 'errors', '')).toHaveLength(1);
    const failed = logEntries(
      [],
      { converged: false, iterations: 50, residualNorm: 1, elapsedMs: 14, sizingPasses: 1 },
      null,
    );
    expect(failed[0]).toMatchObject({
      severity: 'error',
      message: 'Did not converge after 50 iterations',
    });
  });

  it('filters by severity and by text, the group by its members too', () => {
    const entries = logEntries(brief, null, null);
    expect(visibleEntries(entries, 'errors', '')).toHaveLength(1);
    // 'all' shows the six inference notices as one group (they share a code), so four lines.
    expect(visibleEntries(entries, 'all', '')).toHaveLength(4);
    expect(visibleEntries(entries, 'all', 'N3').map((e) => e.subject)).toEqual(['N3', '6×']);
    expect(visibleEntries(entries, 'warnings', 'dead end')).toHaveLength(2);
  });

  it('copies as text with code, severity, component and message, members on their own lines', () => {
    const text = logAsText(logEntries(brief.slice(6), null, null));
    expect(text.split('\n')[0]).toBe("FS1507\terror\tPU1\t'PU1' is not connected to anything.");
    expect(text.split('\n')).toHaveLength(3);
  });

  it("names the solver's state in the header per 56's table", () => {
    const solve = {
      converged: true,
      iterations: 4,
      residualNorm: 0,
      elapsedMs: 14,
      sizingPasses: 1,
    };
    expect(
      headerStatus({ compiling: false, offline: false, errors: 0, solve, totalMs: 14 }),
    ).toEqual({ text: 'solved in 14 ms', severity: 'ok' });
    expect(
      headerStatus({ compiling: true, offline: false, errors: 0, solve, totalMs: 14 }).text,
    ).toBe('solving…');
    expect(
      headerStatus({ compiling: false, offline: false, errors: 3, solve, totalMs: 14 }).text,
    ).toBe('3 errors');
    expect(
      headerStatus({ compiling: false, offline: false, errors: 1, solve: null, totalMs: 14 }).text,
    ).toBe('not solved — 1 error');
    expect(
      headerStatus({
        compiling: false,
        offline: false,
        errors: 0,
        solve: { ...solve, converged: false },
        totalMs: 14,
      }).text,
    ).toBe('did not converge');
    expect(
      headerStatus({ compiling: false, offline: true, errors: 0, solve, totalMs: 14 }).text,
    ).toBe('offline');
  });
});

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import type { Metadata } from '../../api/types.ts';
import { solvedGoldens } from '../../test/goldens.ts';
import { bindingCard, componentCard, connectionCard, formatValue, quantityCard } from './card.ts';

const metadata = JSON.parse(
  readFileSync(
    fileURLToPath(
      new URL(
        '../../../../tests/FluidScript.Api.Tests/Contracts/Goldens/metadata.json',
        import.meta.url,
      ),
    ),
    'utf8',
  ),
) as Metadata;

const loop = solvedGoldens().find((g) => g.name === 'm2-cooling-loop')!.model;

describe('the hover card', () => {
  it('lists every parameter with stated, sized or default and the basis under a chosen one (54)', () => {
    const card = componentCard(loop, '3WV', loop.diagnostics)!;
    expect(card.title).toBe('100TV01');
    expect(card.subtitle).toBe('3WV · three_way_valve');
    const sources = new Set(card.parameters.map((p) => p.source));
    expect([...sources].every((s) => s === 'stated' || s === 'sized' || s === 'default')).toBe(
      true,
    );
    const sized = card.parameters.find((p) => p.source !== 'stated')!;
    expect(sized.basis).toBeDefined();
    expect(card.parameters.find((p) => p.source === 'stated')?.basis).toBeUndefined();
    expect(card.state.map((r) => r.label)).toContain('flow');
  });

  it('says so for an inferred component and shows its warnings', () => {
    const card = componentCard(loop, 'HE1__3WV', [
      {
        code: 'FS1507',
        severity: 'warning',
        message: 'w',
        range: null,
        component: 'HE1__3WV',
        suggestion: null,
        related: [],
      },
      {
        code: 'FS1510',
        severity: 'info',
        message: 'i',
        range: null,
        component: 'HE1__3WV',
        suggestion: null,
        related: [],
      },
    ])!;
    expect(card.inferred).toBe(true);
    expect(card.note).toContain('Added by the compiler');
    expect(card.warnings).toEqual([{ severity: 'warning', message: 'w' }]);
    expect(componentCard(loop, 'nobody', [])).toBeNull();
  });

  it('shows a connection with its flow and the pipe the line carries', () => {
    const c9 = connectionCard(loop, 'c9')!;
    expect(c9.title).toBe('3WV__N3.out – N3');
    expect(c9.subtitle).toBe('pipe 3WV__N3');
    expect(c9.parameters.map((p) => p.label)).toContain('length');
    expect(c9.state.map((r) => r.label)).toEqual(expect.arrayContaining(['flow']));
    expect(connectionCard(loop, 'c0')!.subtitle).toBe('connection');
  });

  it('shows a pipe run with its velocity and Reynolds number from the wire (54, A-6)', () => {
    const pipe = loop.components.find((c) => c.kind === 'pipe' && c.state?.velocity != null)!;
    const connection = loop.connections.find(
      (c) => c.from.component === pipe.id || c.to.component === pipe.id,
    )!;
    const card = connectionCard(loop, connection.id)!;
    expect(card.state.map((r) => r.label)).toEqual(
      expect.arrayContaining(['flow', 'velocity', 'Re', 'Δp']),
    );
    expect(card.state.find((r) => r.label === 'velocity')!.unit).toBe('m/s');
    expect(componentCard(loop, pipe.id, [])!.state.map((r) => r.label)).toContain('Re');
  });

  it('converts a quantity literal to SI and to the other units of its dimension (52, A-6)', () => {
    const card = quantityCard('30 kW', metadata)!;
    expect(card.subtitle).toContain('Power');
    expect(card.state[0]).toEqual({ label: 'SI', value: '30000', unit: 'W' });
    expect(card.state.map((r) => r.unit)).not.toContain('kW');
    expect(card.state.map((r) => r.unit)).toContain('MW');
    expect(card.state.find((r) => r.unit === 'MW')!.value).toBe('0.03');

    const celsius = quantityCard('20 C', metadata)!;
    expect(celsius.state[0]).toMatchObject({ label: 'SI', unit: 'K' });
    expect(Number(celsius.state[0]!.value)).toBeCloseTo(293.15, 1);
    expect(quantityCard('3 furlongs', metadata)).toBeNull();
  });

  it('shows a let with its value or that it waits for the solve', () => {
    const model = {
      ...loop,
      bindings: [
        { name: 'dT', value: 30, unit: 'dK', dimension: 'TemperatureDelta', siUnit: null },
        { name: 'x', value: null, unit: null, dimension: null, siUnit: null },
      ],
    };
    expect(bindingCard(model, 'dT')).toMatchObject({
      subtitle: 'let · TemperatureDelta',
      state: [{ value: '30', unit: 'dK' }],
    });
    expect(bindingCard(model, 'x')!.subtitle).toContain('deferred');
    expect(bindingCard(model, 'nope')).toBeNull();
  });

  it('formats readouts to four significant digits without trailing zeros', () => {
    expect(formatValue(0.430672)).toBe('0.4307');
    expect(formatValue(30.0014)).toBe('30');
    expect(formatValue(1234.5)).toBe('1235');
    expect(formatValue(null)).toBe('—');
    expect(formatValue(0)).toBe('0');
  });
});

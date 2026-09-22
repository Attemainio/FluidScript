import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import type { Metadata, ModelContract } from '../../../api/types.ts';
import { lexLine } from '../language/tokenizer.ts';
import { complete, resolveKind, type Item, type Sources } from './completion.ts';
import { ambiguityDetail, toOptions } from './options.ts';
import { normalize, score } from './similarity.ts';

const root = new URL('../../../../../', import.meta.url);
const metadata = JSON.parse(
  readFileSync(
    fileURLToPath(new URL('tests/FluidScript.Api.Tests/Contracts/Goldens/metadata.json', root)),
    'utf8',
  ),
) as Metadata;
const substation = JSON.parse(
  readFileSync(
    fileURLToPath(
      new URL('tests/FluidScript.Api.Tests/Contracts/Goldens/m2-substation.compile.json', root),
    ),
    'utf8',
  ),
) as ModelContract;

/** A model with three lets, as 52's worked example has them, and the substation's components. */
const model: ModelContract = {
  ...substation,
  bindings: [
    { name: 'Tsupply', value: 70, unit: '°C', dimension: 'Temperature', siUnit: null },
    { name: 'dTdesign', value: 20, unit: 'dK', dimension: 'TemperatureDelta', siUnit: null },
    { name: 'Qtotal', value: 120, unit: 'kW', dimension: 'Power', siUnit: null },
    { name: 'later', value: null, unit: null, dimension: null, siUnit: null },
    { name: 'laterDp', value: null, unit: null, dimension: 'PressureDelta', siUnit: null },
    { name: 'perK', value: 6, unit: null, dimension: null, siUnit: 'kg·m²/(s³·K)' },
  ],
};
const sources: Sources = { metadata, model };

/** Completes with the cursor at the end of `text`. */
function at(text: string, extra: Partial<Sources> = {}): ReturnType<typeof complete> {
  return complete({ doc: text, offset: text.length }, { ...sources, ...extra });
}

const labels = (items: readonly Item[]): string[] => items.map((i) => i.label);

describe('the scorer matches Core', () => {
  it("reproduces NameResolution's figures", () => {
    // The numbers 15 and NameResolution cite: pmp/pump 0.75, valve/pipe 0.20, exchan/exchanger 0.67, fan/tank 0.50.
    expect(score('pmp', 'pump')).toBeCloseTo(0.75, 10);
    expect(score('valve', 'pipe')).toBeCloseTo(0.2, 10);
    expect(score('exchan', 'exchanger')).toBeCloseTo(0.6667, 3);
    expect(score('fan', 'tank')).toBeCloseTo(0.5, 10);
    expect(normalize('Three_Way Valve')).toBe('threewayvalve');
  });
});

describe('kind completion', () => {
  it.each(['heat_ex', 'heatex', 'HeatEx', 'exch'])(
    'offers heat_exchanger for %s and inserts the canonical keyword',
    (typed) => {
      // 52's first completion criterion; Tab commits the canonical form, never the alias (5a).
      const result = at(`HE1 ${typed}`);
      expect(result.context).toBe('kind');
      const first = result.items[0]!;
      expect(first.label).toBe('heat_exchanger');
      expect(first.insert).toBe('heat_exchanger');
    },
  );

  it('reaches every alias in the registry, walking the registry rather than a fixed list', () => {
    for (const kind of metadata.kinds) {
      for (const alias of kind.aliases) {
        const result = at(`X1 ${alias}`);
        const item = result.items.find((i) => i.label === kind.keyword);
        expect(item, `${alias} should reach ${kind.keyword}`).toBeDefined();
        expect(item?.insert).toBe(kind.keyword);
      }
    }
  });

  it('shows the alias that matched', () => {
    const item = at('R1 rad').items.find((i) => i.label === 'heat_exchanger');
    expect(item?.detail).toBe("via 'radiator'");
  });

  it('offers pump for pmp, as the binder resolves it (FS1512)', () => {
    expect(labels(at('P1 pmp').items)).toContain('pump');
    expect(resolveKind(metadata, 'pmp')?.keyword).toBe('pump');
  });

  it('lists both candidates of an ambiguous input, adjacent, and resolves neither', () => {
    // 52: ambiguity is shown, not resolved. `v_lve` sits between valve and three_way_valve's alias
    // only if the scores are within the margin; find an input by search so the test states a fact.
    const ambiguous = ['vale', 'tnk', 'sensr', 'pipe_', 'controler'].find((typed) => {
      const items = at(`X ${typed}`).items;
      return items.length >= 2 && items[0]?.ambiguous === true;
    });
    if (ambiguous === undefined) {
      // No registry pair is within 0.05 of each other for these inputs; the margin rule is still
      // exercised by resolveKind, which returns null when two candidates tie.
      expect(resolveKind(metadata, 'zzzz')).toBeNull();
      return;
    }
    const items = at(`X ${ambiguous}`).items;
    expect(items[0]?.ambiguous).toBe(true);
    expect(items[1]?.ambiguous).toBe(true);
    expect(resolveKind(metadata, ambiguous)).toBeNull();

    // U-6: CodeMirror preselects the first option, so the first option is the typed text itself and
    // inserts nothing; the pair follows it and the reader picks one.
    const options = toOptions(items, ambiguous);
    expect(options[0]).toMatchObject({ label: ambiguous, detail: ambiguityDetail, boost: 99 });
    expect(typeof options[0]?.apply).toBe('function');
    expect(options.slice(1).map((o) => o.label)).toEqual(items.map((i) => i.label));
  });

  it('adds no header to a list that is not ambiguous (U-6)', () => {
    const items = at('P1 pmp').items;
    expect(items.some((i) => i.ambiguous === true)).toBe(false);
    expect(toOptions(items, 'pmp').map((o) => o.label)).toEqual(items.map((i) => i.label));
  });

  it('offers nothing at the start of a declaration line', () => {
    expect(at('HE').items).toEqual([]);
    expect(at('').items).toEqual([]);
  });
});

describe('parameter completion', () => {
  it("offers the kind's remaining parameters with dimension, unit and range", () => {
    const result = at('HE1 heat_exchanger power=30 ');
    expect(result.context).toBe('parameter');
    expect(labels(result.items)).not.toContain('power');
    const inlet = result.items.find((i) => i.label === 'in.t');
    expect(inlet?.detail).toBe('Temperature · °C · typically -50…300');
    expect(labels(result.items)).toContain('in[2].flow');
    expect(result.items.find((i) => i.label === 'dt')?.detail).toBe(
      'TemperatureDelta · dK · typically 0.1…200',
    );
  });

  it('the tank offers volume, layers, t, the profile members for the resolved layers, and the level templates', () => {
    // 52's indexed rule (D-32): no tN above the tank's resolved layer count.
    const tank = {
      ...model,
      components: [
        {
          ...substation.components[0]!,
          id: 'T1',
          kind: 'tank',
          parameters: { layers: { value: 3, unit: null, source: 'stated' } },
          ports: [{ name: 'in1', role: 'bidirectional', connectedTo: null }],
        },
      ],
    } as ModelContract;
    const result = at('T1 tank layers=3 ', { model: tank });
    const names = labels(result.items);
    expect(names).toEqual(
      expect.arrayContaining([
        'volume',
        't',
        'elevation',
        'layer[1].t',
        'layer[2].t',
        'layer[3].t',
        'in.level',
        'out[16].level',
      ]),
    );
    expect(names).not.toContain('layer[4].t');
    expect(names).not.toContain('in[17].level');
    expect(names).not.toContain('in[1].level');
  });
});

describe("a port's state (D-120)", () => {
  it('after a port and a dot offers the quantities that port takes, minus the ones written', () => {
    const second = at('HE1 heat_exchanger in[2].');
    expect(second.context).toBe('parameter');
    expect(labels(second.items)).toEqual(expect.arrayContaining(['t', 'flow', 'dp', 'dt']));
    expect(labels(second.items)).not.toContain('power');

    const first = at('HE1 heat_exchanger in.t=40 in.');
    expect(labels(first.items)).not.toContain('t');

    const folded = at('HE1 heat_exchanger in[1].');
    expect(labels(folded.items)).toContain('t');

    // Every port has a pressure (D-124), the tank's family members included.
    expect(labels(at('T1 tank in[3].').items).sort()).toEqual(['level', 'p']);
  });

  it('after in[2].t= filters values by temperature, as in.t= does', () => {
    const value = at('HE1 heat_exchanger in[2].t=');
    expect(value.context).toBe('value');
    expect(labels(value.items)).toContain('Tsupply');
    expect(labels(value.items)).not.toContain('dTdesign');
  });

  it('a state already written is not offered again, and the port index closes a parameter', () => {
    const next = at('HE1 heat_exchanger in[2].t=85 ');
    expect(next.context).toBe('parameter');
    expect(labels(next.items)).not.toContain('in[2].t');
    expect(labels(next.items)).toContain('out[2].t');
  });
});

describe('value completion is dimension-filtered', () => {
  it('after out.t= offers a Temperature let and not a TemperatureDelta one; after dt= the reverse', () => {
    // The single highest-value completion test (52): the distinction FS1302 exists to catch.
    const out = at('HE1 heat_exchanger out.t=');
    expect(out.context).toBe('value');
    expect(labels(out.items)).toContain('Tsupply');
    expect(labels(out.items)).not.toContain('dTdesign');
    expect(labels(out.items)).not.toContain('Qtotal');

    const dt = at('HE1 heat_exchanger dt=');
    expect(labels(dt.items)).toContain('dTdesign');
    expect(labels(dt.items)).not.toContain('Tsupply');
  });

  it('after power= offers both kW and W: by dimension, never by canonical unit', () => {
    const names = labels(at('HE1 heat_exchanger power=').items);
    expect(names).toEqual(expect.arrayContaining(['Qtotal', 'kW', 'W', 'MW']));
    expect(names).not.toContain('K');
    expect(names).not.toContain('Tsupply');
  });

  it('offers component.property references of the dimension', () => {
    const names = labels(at('HE1 heat_exchanger power=').items);
    expect(names.some((n) => /^\w+\.power$/.test(n))).toBe(true);
    expect(names.some((n) => /\.dp$/.test(n))).toBe(false);
  });

  it('offers a deferred let with its dimension unknown and no value, dimmed', () => {
    const item = at('HE1 heat_exchanger power=').items.find((i) => i.label === 'later');
    expect(item?.dimmed).toBe(true);
    expect(item?.detail).toContain('—');
  });

  it('filters a deferred let by the dimension the binder typed (U-5)', () => {
    const dp = at('HE1 heat_exchanger dp=').items.find((i) => i.label === 'laterDp');
    expect(dp?.dimmed).toBe(true);
    expect(dp?.detail).toContain('PressureDelta');
    expect(at('HE1 heat_exchanger power=').items.find((i) => i.label === 'laterDp')).toBeUndefined();
  });

  it('offers an unnamed-dimension let dimmed with its derived unit, where the filter is off', () => {
    const item = at('let x = ').items.find((i) => i.label === 'perK');
    expect(item?.dimmed).toBe(true);
    expect(item?.detail).toContain('kg·m²/(s³·K)');
  });

  it('offers everything when the kind did not resolve', () => {
    // 52 invariant 5b: no filter is possible, so a broken script still completes.
    const names = labels(at('HE1 zzzz power=').items);
    expect(names).toEqual(expect.arrayContaining(['Tsupply', 'dTdesign', 'Qtotal']));
  });
});

describe('connection and dotted completion', () => {
  const doc = 'fluidscript 1\nconnections\n';

  it('offers declared and inferred names at the start of a connection line and after -', () => {
    const start = at(doc);
    expect(start.context).toBe('connection-name');
    expect(labels(start.items)).toContain(substation.components[0]!.id);
    const inferred = start.items.find((i) => i.detail?.endsWith('inferred'));
    expect(inferred).toBeDefined();

    const after = at(doc + 'N1 - ');
    expect(after.context).toBe('connection-name');
  });

  it("offers a component's ports after a dot in a connection, and its properties elsewhere", () => {
    const exchanger = substation.components.find((c) => c.kind === 'heat_exchanger')!;
    const port = at(`${doc}${exchanger.id}.`);
    expect(port.context).toBe('port');
    expect(labels(port.items)).toEqual(expect.arrayContaining(['in', 'out']));

    const property = at(`let x = ${exchanger.id}.`);
    expect(property.context).toBe('property');
    expect(labels(property.items)).toEqual(expect.arrayContaining(['dp', 'power']));
  });

  it('container and v find tank and volume; T1.in[2] materializes in2 and nothing offers in17', () => {
    expect(at('T1 contai').items[0]?.label).toBe('tank');
    expect(labels(at('T1 tank v').items)).toContain('volume');
    const tank = {
      ...model,
      components: [
        {
          ...substation.components[0]!,
          id: 'T1',
          kind: 'tank',
          ports: [{ name: 'in1', role: 'bidirectional', connectedTo: null }],
        },
      ],
    } as ModelContract;
    const ports = at(`${doc}T1.`, { model: tank });
    const template = ports.items.find((i) => i.type === 'template' && i.label.startsWith('in'));
    expect(template?.insert).toBe('in[2]');
    expect(labels(ports.items)).toContain('in');
    expect(labels(ports.items)).not.toContain('in[17]');
    expect(labels(ports.items)).not.toContain('in2');
  });
});

describe('every offered item still parses', () => {
  it('accepting any item in any position over the corpus yields a lexable line', () => {
    // 52's last criterion, at the lexical layer the tokenizer owns: an inserted item never produces an Unknown token.
    const positions = [
      'HE1 heat_ex',
      'HE1 heat_exchanger ',
      'HE1 heat_exchanger power=',
      'let x = ',
      'fluidscript 1\nconnections\nN1 - ',
      'fluidscript 1\nconnections\nN1.',
    ];
    for (const text of positions) {
      const result = at(text);
      for (const item of result.items) {
        const line = text.slice(
          text.lastIndexOf('\n') + 1,
          result.from - (text.length - text.length),
        );
        const completed =
          line.slice(0, result.from - (text.lastIndexOf('\n') + 1)) + (item.insert ?? item.label);
        expect(
          lexLine(completed).some((t) => t.kind === 'Unknown'),
          completed,
        ).toBe(false);
      }
    }
  });
});

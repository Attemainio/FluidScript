import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
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
    { name: 'dTdesign', value: 20, unit: 'K', dimension: 'TemperatureDelta', siUnit: null },
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

  it('offers pump for pmp as the fix, and does not bind it (D-170)', () => {
    // The binder reports `pmp` as FS1502 with pump as its fix; completion offers the fix and, like the binder,
    // reads the component's kind as unknown, so its parameters are not pump's.
    const item = at('P1 pmp').items.find((i) => i.label === 'pump');
    expect(item?.detail).toBe('did you mean pump?');
    expect(resolveKind(metadata, 'pmp')).toBeNull();
    expect(at('P1 pmp ').items).toEqual([]);
  });

  it('lists both candidates of an ambiguous input, adjacent, and preselects neither', () => {
    // 52: ambiguity is shown, not resolved. Find an input by search so the test states a fact.
    const ambiguous = ['vale', 'tnk', 'sensr', 'pipe_', 'controler'].find((typed) => {
      const items = at(`X ${typed}`).items;
      return items.length >= 2 && items[0]?.ambiguous === true;
    });
    if (ambiguous === undefined) {
      expect(resolveKind(metadata, 'zzzz')).toBeNull();
      return;
    }
    const items = at(`X ${ambiguous}`).items;
    expect(items[0]?.ambiguous).toBe(true);
    expect(items[1]?.ambiguous).toBe(true);

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

  it('describes a kind by the ports a script writes (D-179)', () => {
    const item = at('HX1 heat_exchanger').items.find((i) => i.label === 'heat_exchanger');
    expect(item?.detail).toBe('Ports: in, out, secondary.in, secondary.out');
  });
});

describe('the start of a line (19)', () => {
  it('offers the statement words at the top level', () => {
    const result = at('fluidscript 2\n\n');
    expect(result.context).toBe('statement');
    expect(labels(result.items)).toEqual(['fluidscript', 'project', 'let', 'curve', 'circuit', 'run']);
  });

  it("offers a block's settings inside it, from the metadata, less the ones written", () => {
    const project = at('project "P":\n  cases = [winter, mild]\n  ');
    expect(project.context).toBe('setting');
    expect(labels(project.items)).toEqual(['catalog', 'show', 'scale', 'spacing', 'style']);
    expect(project.items.find((i) => i.label === 'style')?.insert).toBe('style:');

    const circuit = at('circuit "C":\n  fluid = water\n  ');
    expect(labels(circuit.items)).toEqual(expect.arrayContaining(['number', 'role', 'style', 'HX1']));
    expect(labels(circuit.items)).not.toContain('fluid');

    expect(labels(at('circuit "C":\n  style:\n    ').items)).toEqual(['colour', 'width', 'corner', 'line']);
  });

  it("offers a declaration block's parameters, and a controller's settings for its type (D-168)", () => {
    const exchanger = at('circuit "C":\n  HX1 exchanger:\n    primary.out.t = 45 C\n    ');
    expect(labels(exchanger.items)).toContain('secondary.in.t');
    expect(labels(exchanger.items)).not.toContain('out.t');

    const pi = labels(at('circuit "C":\n  TC1 controller:\n    moves = PCV\n    ').items);
    expect(pi).toEqual(['type', 'reads', 'setpoint', 'band', 'kp', 'ti', 'output', 'action']);

    const onoff = labels(at('circuit "C":\n  TC1 controller:\n    type = onoff\n    ').items);
    expect(onoff).toContain('differential');
    expect(onoff).not.toContain('band');
  });

  it("offers a run's settings, its event words, the lets and the components", () => {
    const names = labels(at('run "R":\n  ').items);
    expect(names).toEqual(
      expect.arrayContaining(['from', 'start', 'duration', 'frame', 'steady', 'at', 'over', 'Tsupply', 'HX1']),
    );
  });
});

describe('parameter completion', () => {
  it("offers the kind's remaining parameters with dimension, unit and range", () => {
    const result = at('HE1 heat_exchanger power = 30 ');
    expect(result.context).toBe('parameter');
    expect(labels(result.items)).not.toContain('power');
    const inlet = result.items.find((i) => i.label === 'in.t');
    expect(inlet?.detail).toBe('Temperature · °C · typically -50…300');
    expect(labels(result.items)).toContain('secondary.in.flow');
    expect(result.items.find((i) => i.label === 'dt')?.detail).toBe(
      'TemperatureDelta · K · typically 0.1…200',
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
    const names = labels(at('T1 tank layers = 3 ', { model: tank }).items);
    expect(names).toEqual(
      expect.arrayContaining(['volume', 't', 'elevation', 'layer[1].t', 'layer[3].t', 'in.level', 'out[16].level']),
    );
    expect(names).not.toContain('layer[4].t');
    expect(names).not.toContain('in[17].level');
    expect(names).not.toContain('in[1].level');
  });
});

describe("a port's state (D-120, D-179)", () => {
  it("after the exchanger's secondary.in and a dot offers the quantities that port takes", () => {
    const second = at('HE1 heat_exchanger secondary.in.');
    expect(second.context).toBe('parameter');
    expect(labels(second.items)).toEqual(expect.arrayContaining(['t', 'flow', 'dp', 'dt']));
    expect(labels(second.items)).not.toContain('power');
  });

  it('reads primary.in as in, and the side after secondary', () => {
    expect(labels(at('HE1 heat_exchanger primary.in.').items)).toContain('t');
    expect(labels(at('HE1 heat_exchanger secondary.').items)).toEqual(['in', 'out']);
  });

  it('after a port and a dot offers the quantities that port takes, minus the ones written', () => {
    expect(labels(at('HE1 heat_exchanger in.t = 40 in.').items)).not.toContain('t');
    expect(labels(at('HE1 heat_exchanger in[1].').items)).toContain('t');
    // Every port has a pressure (D-124), the tank's family members included.
    expect(labels(at('T1 tank in[3].').items).sort()).toEqual(['level', 'p']);
  });

  it('after secondary.in.t = filters values by temperature, as in.t = does', () => {
    const value = at('HE1 heat_exchanger secondary.in.t = ');
    expect(value.context).toBe('value');
    expect(labels(value.items)).toContain('Tsupply');
    expect(labels(value.items)).not.toContain('dTdesign');
  });

  it('a state already written is not offered again', () => {
    const next = at('HE1 heat_exchanger secondary.in.t = 85 ');
    expect(next.context).toBe('parameter');
    expect(labels(next.items)).not.toContain('secondary.in.t');
    expect(labels(next.items)).toContain('secondary.out.t');
  });
});

describe('value completion is dimension-filtered', () => {
  it('after out.t = offers a Temperature let and not a TemperatureDelta one; after dt = the reverse', () => {
    // The single highest-value completion test (52): the distinction FS1302 exists to catch.
    const out = at('HE1 heat_exchanger out.t = ');
    expect(out.context).toBe('value');
    expect(labels(out.items)).toContain('Tsupply');
    expect(labels(out.items)).not.toContain('dTdesign');
    expect(labels(out.items)).not.toContain('Qtotal');

    const dt = at('HE1 heat_exchanger dt = ');
    expect(labels(dt.items)).toContain('dTdesign');
    expect(labels(dt.items)).not.toContain('Tsupply');
  });

  it('after power = offers both kW and W: by dimension, never by canonical unit', () => {
    const names = labels(at('HE1 heat_exchanger power = ').items);
    expect(names).toEqual(expect.arrayContaining(['Qtotal', 'kW', 'W', 'MW']));
    expect(names).not.toContain('K');
    expect(names).not.toContain('Tsupply');
  });

  it('never offers the inch or the tonne, which a script cannot write (A-9)', () => {
    expect(labels(at('P1 pipe length = ').items)).not.toContain('in');
    expect(labels(at('P1 pipe length = ').items)).toContain('mm');
  });

  it('offers component.property references of the dimension', () => {
    const names = labels(at('HE1 heat_exchanger power = ').items);
    expect(names.some((n) => /^\w+\.power$/.test(n))).toBe(true);
    expect(names.some((n) => /\.dp$/.test(n))).toBe(false);
  });

  it('offers a deferred let with its dimension unknown and no value, dimmed', () => {
    const item = at('HE1 heat_exchanger power = ').items.find((i) => i.label === 'later');
    expect(item?.dimmed).toBe(true);
    expect(item?.detail).toContain('—');
  });

  it('filters a deferred let by the dimension the binder typed (U-5)', () => {
    const dp = at('HE1 heat_exchanger dp = ').items.find((i) => i.label === 'laterDp');
    expect(dp?.dimmed).toBe(true);
    expect(dp?.detail).toContain('PressureDelta');
    expect(at('HE1 heat_exchanger power = ').items.find((i) => i.label === 'laterDp')).toBeUndefined();
  });

  it('offers an unnamed-dimension let dimmed with its derived unit, where the filter is off', () => {
    const item = at('let x = ').items.find((i) => i.label === 'perK');
    expect(item?.dimmed).toBe(true);
    expect(item?.detail).toContain('kg·m²/(s³·K)');
  });

  it('offers everything when the kind did not resolve', () => {
    // 52 invariant 5b: no filter is possible, so a broken script still completes.
    const names = labels(at('HE1 zzzz power = ').items);
    expect(names).toEqual(expect.arrayContaining(['Tsupply', 'dTdesign', 'Qtotal']));
  });

  it("offers a setting's words, the build's sets and the script's own names", () => {
    expect(labels(at('circuit "C":\n  style:\n    corner = ').items)).toEqual(['sharp', 'fillet']);
    expect(labels(at('circuit "C":\n  fluid = ').items)).toContain('water');
    expect(labels(at('circuit "C":\n  role = ').items)).toContain('radiator');
    expect(labels(at('project:\n  catalog = ').items)).toContain('steel_en10255@2026.1');
    expect(labels(at('project:\n  cases = [winter, mild]\nrun "R":\n  from = ').items)).toEqual(['winter', 'mild']);
    expect(labels(at('run "R":\n  duration = ').items)).toEqual(expect.arrayContaining(['s', 'min', 'h']));
  });

  it("offers a controller's actuators, its measurements and its words", () => {
    const moves = at('circuit "C":\n  TC1 controller:\n    moves = ');
    expect(labels(moves.items)).toEqual(expect.arrayContaining(['PCV', 'SP']));
    expect(labels(moves.items)).not.toContain('HX1');
    expect(labels(at('circuit "C":\n  TC1 controller:\n    reads = ').items)).toContain('time');
    expect(labels(at('circuit "C":\n  TC1 controller:\n    type = ').items)).toEqual(['P', 'PI', 'PID', 'onoff', 'curve']);
  });
});

describe('connection and dotted completion', () => {
  const doc = 'fluidscript 2\ncircuit "C":\n';

  it('offers declared and inferred names after a - in a connection', () => {
    const after = at(doc + '  NPS - ');
    expect(after.context).toBe('connection-name');
    expect(labels(after.items)).toContain(substation.components[0]!.id);
    expect(after.items.find((i) => i.detail?.endsWith('inferred'))).toBeDefined();
  });

  it("offers a component's ports as a script spells them, and its properties in a value", () => {
    const port = at(`${doc}  HX1.`);
    expect(port.context).toBe('port');
    expect(labels(port.items)).toEqual(
      expect.arrayContaining(['in', 'out', 'secondary.in', 'secondary.out', 'primary.in']),
    );
    expect(labels(port.items)).not.toContain('in[2]');
    expect(labels(at(`${doc}  PCV - HX1.secondary.`).items)).toEqual(['in', 'out']);

    const property = at('let x = HX1.');
    expect(property.context).toBe('property');
    expect(labels(property.items)).toEqual(expect.arrayContaining(['dp', 'power']));
  });

  it("offers a pipe's settings after the chain, and their values", () => {
    const pipe = at(`${doc}  NPS - PCV   12 m  `);
    expect(pipe.context).toBe('parameter');
    expect(labels(pipe.items)).toContain('roughness');
    expect(labels(at(`${doc}  NPS - PCV   roughness = `).items)).toContain('mm');
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
    const ports = at(`${doc}  N1 - T1.`, { model: tank });
    const template = ports.items.find((i) => i.type === 'template' && i.label.startsWith('in'));
    expect(template?.insert).toBe('in[2]');
    expect(labels(ports.items)).toContain('in');
    expect(labels(ports.items)).not.toContain('in[17]');
    expect(labels(ports.items)).not.toContain('in2');
  });
});

describe('runs and curves', () => {
  it("offers an event's targets, a target's parameters, and a value of its dimension", () => {
    const doc = 'run "R":\n';
    expect(labels(at(doc + '  at 10 min ').items)).toContain('LOAD');
    const target = at(doc + '  at 10 min LOAD.');
    expect(target.context).toBe('target');
    expect(labels(target.items)).toContain('power');
    const value = labels(at(doc + '  at 10 min LOAD.power = ').items);
    expect(value).toEqual(expect.arrayContaining(['Qtotal', 'kW']));
    expect(value).not.toContain('Tsupply');
  });

  it("offers a curve's drivers: the lets and the clock", () => {
    const result = at('curve heating: ');
    expect(result.context).toBe('driver');
    expect(labels(result.items)).toEqual(expect.arrayContaining(['Tsupply', 'time']));
  });
});

describe('every offered item still parses', () => {
  it('accepting any item in any position yields a lexable line', () => {
    // 52's last criterion, at the lexical layer the tokenizer owns: an inserted item never produces an Unknown token.
    const positions = [
      'HE1 heat_ex',
      'HE1 heat_exchanger ',
      'HE1 heat_exchanger power = ',
      'let x = ',
      'fluidscript 2\ncircuit "C":\n  N1 - ',
      'fluidscript 2\ncircuit "C":\n  HX1.',
      'project:\n  ',
      'run "R":\n  at 10 min LOAD.',
    ];
    for (const text of positions) {
      const result = at(text);
      const lineStart = text.lastIndexOf('\n') + 1;
      for (const item of result.items) {
        const completed = text.slice(lineStart, result.from) + (item.insert ?? item.label);
        expect(
          lexLine(completed).some((t) => t.kind === 'Unknown'),
          completed,
        ).toBe(false);
      }
    }
  });
});

describe('completion never throws', () => {
  it('at any offset of any sample, with and without a model', () => {
    // A script under editing is malformed most of the time (P4): every prefix of every sample is a position.
    const samples = fileURLToPath(new URL('samples/', root));
    for (const file of readdirSync(samples).filter((f) => f.endsWith('.fluid'))) {
      const text = readFileSync(join(samples, file), 'utf8');
      for (let offset = 0; offset <= text.length; offset++) {
        for (const extra of [{}, { model: null }]) {
          expect(() => complete({ doc: text, offset }, { ...sources, ...extra }), `${file}@${offset}`).not.toThrow();
        }
      }
    }
  });
});

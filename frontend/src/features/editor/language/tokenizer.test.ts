import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { initialState, lexLine, tokenizeLine, type LineToken } from './tokenizer.ts';

const root = fileURLToPath(new URL('../../../../../', import.meta.url));
const samples = join(root, 'samples');
const goldens = join(
  root,
  'tests',
  'FluidScript.Core.Tests',
  'Language',
  'Syntax',
  'Lexing',
  'TokenGoldens',
);

function roles(line: string, state = initialState()): string {
  return tokenizeLine(line, state)
    .map((t: LineToken) => `${t.text}:${t.role}`)
    .join(' ');
}

describe('the tokenizer follows 12 and 19', () => {
  it('reads a word that starts with a digit as a name, and one that ends in a unit as a quantity', () => {
    // 12 rules 3 and 4: 3WV is a name because WV is not a unit; 20C is twenty degrees.
    expect(roles('3WV valve3 kvs = 20C')).toBe(
      '3WV:declaration valve3:kind kvs:parameter =:operator 20:number C:unit',
    );
  });

  it('joins a spaced unit to its number unless = follows, past spaces too', () => {
    // Rule 5, looking past spaces (19): `30 K` is one quantity; in `flow = 5 h = 2000` the h is the next parameter.
    expect(roles('let rise = 30 K')).toBe('let:keyword rise:declaration =:operator 30:number K:unit');
    expect(roles('P1 pump flow = 5 h = 2000')).toBe(
      'P1:declaration pump:kind flow:parameter =:operator 5:number h:parameter =:operator 2000:number',
    );
  });

  it('reads no inch and no tonne after a number (19)', () => {
    expect(lexLine('30 in').map((t) => t.kind)).toEqual(['NumberLiteral', 'Identifier']);
    expect(lexLine('p = 300 t = 6').map((t) => t.kind)).toEqual([
      'Identifier',
      'Punctuation',
      'NumberLiteral',
      'Identifier',
      'Punctuation',
      'NumberLiteral',
    ]);
  });

  it("names every part of a port's state, and the exchanger's sides", () => {
    expect(roles('HX1 exchanger power = 150 primary.in.t = 40 secondary.out.t = 45 C')).toBe(
      'HX1:declaration exchanger:kind power:parameter =:operator 150:number primary:parameter .:operator in:parameter .:operator t:parameter =:operator 40:number secondary:parameter .:operator out:parameter .:operator t:parameter =:operator 45:number C:unit',
    );
  });

  it('lexes a date and a clock time as one token each', () => {
    expect(lexLine('2026-01-15 06:00   -18').map((t) => `${t.kind}:${t.text}`)).toEqual([
      'DateLiteral:2026-01-15 06:00',
      'Punctuation:-',
      'NumberLiteral:18',
    ]);
    expect(lexLine('06:30').map((t) => t.kind)).toEqual(['DateLiteral']);
  });

  it('takes the longest unit and never parses inside it', () => {
    expect(lexLine('let cp = 4.18 kJ/(kg*K)').map((t) => t.kind)).toEqual([
      'Identifier',
      'Identifier',
      'Punctuation',
      'QuantityLiteral',
    ]);
  });

  it('needs a digit after a dot, so a range is two dots', () => {
    expect(lexLine('over 30..60').map((t) => t.text)).toEqual(['over', '30', '..', '60']);
    expect(lexLine('30.5').map((t) => t.kind)).toEqual(['NumberLiteral']);
  });

  it('knows a statement word by its place: at the start it opens a statement, before = it names a setting', () => {
    expect(roles('circuit "Heating": # the name')).toBe(
      'circuit:keyword "Heating":string ::operator # the name:comment',
    );
    expect(roles('  curve = heating')).toBe('curve:parameter =:operator heating:word');
  });

  it('classifies a line by what follows its first name: a connection, its ports, its pipe', () => {
    expect(roles('  HX1.secondary.out - TV1    30 m  DN32')).toBe(
      'HX1:name .:operator secondary:port .:operator out:port -:operator TV1:name 30:number m:unit DN32:word',
    );
    expect(roles('  HX1 - NPR   8 m   roughness = 0.05 mm')).toBe(
      'HX1:name -:operator NPR:name 8:number m:unit roughness:parameter =:operator 0.05:number mm:unit',
    );
  });

  it('reads a block by indentation: a declaration block, a curve, a run and its events', () => {
    const state = initialState();
    expect(roles('circuit "C":', state)).toBe('circuit:keyword "C":string ::operator');
    expect(roles('  TC1 controller:', state)).toBe('TC1:declaration controller:kind ::operator');
    expect(roles('    band = swing', state)).toBe('band:parameter =:operator swing:word');
    expect(state.blocks.map((b) => b.kind)).toEqual(['circuit', 'declaration']);
    expect(state.blocks[1]).toMatchObject({ name: 'TC1', writtenKind: 'controller' });

    expect(roles('curve heat_demand: outdoor extrapolated', state)).toBe(
      'curve:keyword heat_demand:declaration ::operator outdoor:word extrapolated:keyword',
    );
    expect(roles('  -26   150', state)).toBe('-:operator 26:number 150:number');

    tokenizeLine('run "Cold":', state);
    expect(roles('  at 10 min  RAD.power = 100 kW', state)).toBe(
      'at:keyword 10:number min:unit RAD:parameter .:operator power:parameter =:operator 100:number kW:unit',
    );
    expect(state.blocks.map((b) => b.kind)).toEqual(['run']);
  });

  it('reads a value: a function, a property through a dot', () => {
    expect(roles('let swing = max(rise, 15 K) * HX1.dp')).toBe(
      'let:keyword swing:declaration =:operator max:function (:operator rise:word ,:operator 15:number K:unit ):operator *:operator HX1:word .:operator dp:reference',
    );
  });
});

describe('the tokenizer agrees with the lexer', () => {
  // 52's acceptance: the two grammars agree on token classification over the whole sample corpus.
  // Core writes each sample's tokens (TokenGoldenTests); the same files are read here.
  const files = readdirSync(goldens).filter((f) => f.endsWith('.tokens'));
  expect(files.length).toBeGreaterThan(0);

  it.each(files)('%s', (file) => {
    const script = readFileSync(join(samples, file.replace(/\.tokens$/, '.fluid')), 'utf8');
    const expected = readFileSync(join(goldens, file), 'utf8').trim().split('\n');
    const actual: string[] = [];

    let offset = 0;
    for (const line of script.split(/(?<=\n)/)) {
      const body = line.replace(/\r?\n$/, '');
      for (const token of lexLine(body)) {
        const from = offset + token.from;
        const unit = token.unitFrom === undefined ? '' : ` ${offset + token.unitFrom}`;
        actual.push(
          `${from} ${token.to - token.from} ${token.kind === 'Punctuation' ? punctuationName(token.text) : token.kind}${unit}`,
        );
      }
      offset += line.length;
    }

    expect(actual).toEqual(expected);
  });
});

/** Core names each punctuation token; the tokenizer keeps the text and maps it here. */
function punctuationName(text: string): string {
  switch (text) {
    case '=':
      return 'Equals';
    case '-':
      return 'Minus';
    case '+':
      return 'Plus';
    case '*':
      return 'Star';
    case '/':
      return 'Slash';
    case '.':
      return 'Dot';
    case '..':
      return 'DotDot';
    case ',':
      return 'Comma';
    case '(':
      return 'OpenParenthesis';
    case ')':
      return 'CloseParenthesis';
    case '@':
      return 'At';
    case '[':
      return 'OpenBracket';
    case ']':
      return 'CloseBracket';
    case ':':
      return 'Colon';
    default:
      return 'Unknown';
  }
}

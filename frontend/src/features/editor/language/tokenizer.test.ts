import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { initialState, lexLine, tokenizeLine, type LineToken } from './tokenizer.ts';

const root = fileURLToPath(new URL('../../../../../', import.meta.url));
const samples = join(root, 'samples');
const goldens = join(root, 'tests', 'FluidScript.Core.Tests', 'Syntax', 'TokenGoldens');

function roles(line: string, state = initialState()): string {
  return tokenizeLine(line, state)
    .map((t: LineToken) => `${t.text}:${t.role}`)
    .join(' ');
}

describe('the tokenizer follows 12', () => {
  it('reads a word that starts with a digit as a name, and one that ends in a unit as a quantity', () => {
    // 12 rules 3 and 4: 3WV is a name because WV is not a unit; 20C is twenty degrees.
    expect(roles('3WV three_way_valve kv=20C')).toBe(
      '3WV:declaration three_way_valve:kind kv:parameter =:operator 20:number C:unit',
    );
  });

  it('joins a spaced unit to its number unless = follows', () => {
    // Rule 5: `30 K` is one quantity; `30 in=20` is a number and a parameter named in.
    expect(roles('let dT = 30 K')).toBe('let:keyword dT:declaration =:operator 30:number K:unit');
    expect(roles('HE1 heat_exchanger power=30 in=20')).toBe(
      'HE1:declaration heat_exchanger:kind power:parameter =:operator 30:number in:parameter =:operator 20:number',
    );
  });

  it('takes the longest unit and never parses inside it', () => {
    expect(lexLine('let cp = 4.18 kJ/(kg*K)').map((t) => t.kind)).toEqual([
      'Keyword',
      'Identifier',
      'Punctuation',
      'QuantityLiteral',
    ]);
    expect(lexLine('let mdot = Q / (cp * dT)').map((t) => t.kind)).toEqual([
      'Keyword',
      'Identifier',
      'Punctuation',
      'Identifier',
      'Punctuation',
      'Punctuation',
      'Identifier',
      'Punctuation',
      'Identifier',
      'Punctuation',
    ]);
  });

  it('needs a digit after a dot, so a range is two dots', () => {
    expect(lexLine('over 30..60').map((t) => t.text)).toEqual(['over', '30', '..', '60']);
    expect(lexLine('30.5').map((t) => t.kind)).toEqual(['NumberLiteral']);
  });

  it('classifies by position: a connection, a port, a property reference', () => {
    const state = initialState();
    tokenizeLine('connections', state);
    expect(state.section).toBe('connections');
    expect(roles('3WV.b - N3 length=25', state)).toBe(
      '3WV:name .:operator b:port -:operator N3:name length:parameter =:operator 25:number',
    );
    expect(roles('PU1 pump head=HE1.dp*1.2')).toBe(
      'PU1:declaration pump:kind head:parameter =:operator HE1:word .:operator dp:reference *:operator 1.2:number',
    );
  });

  it('keeps a comment to the end of the line, and a keyword in kind position is a kind', () => {
    expect(roles('circuit loop # the name')).toBe(
      'circuit:keyword loop:declaration # the name:comment',
    );
    expect(roles('S1 inlet t=5')).toBe('S1:declaration inlet:kind t:parameter =:operator 5:number');
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
    case ':':
      return 'Colon';
    default:
      return 'Unknown';
  }
}

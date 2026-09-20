import { reservedWords, unitSymbols } from './lexicon.generated.ts';

/**
 * A token's role, the thing the highlighter colours (`52`'s table). The lexical kinds mirror Core's
 * `TokenKind`; the roles below them are what the position on the line adds: a name being declared,
 * a kind, a parameter, a port, a property read through a dot.
 */
export type TokenRole =
  | 'keyword'
  | 'kind'
  | 'declaration'
  | 'name'
  | 'parameter'
  | 'number'
  | 'unit'
  | 'string'
  | 'comment'
  | 'operator'
  | 'reference'
  | 'port'
  | 'function'
  | 'word'
  | 'unknown';

/** One token on one line. Offsets are code units from the start of the line. */
export interface LineToken {
  readonly from: number;
  readonly to: number;
  readonly role: TokenRole;
  /** The lexical kind as Core's lexer names it, for the agreement test. */
  readonly kind:
    | 'Keyword'
    | 'Identifier'
    | 'NumberLiteral'
    | 'QuantityLiteral'
    | 'StringLiteral'
    | 'Comment'
    | 'Punctuation'
    | 'Unknown';
  readonly text: string;
}

/** The section a line is in (`12` Sections): what the first token of a non-directive line means. */
export type Section = 'declarations' | 'connections' | 'schedule' | 'curve';

/** The state carried from line to line: the section, which is all the grammar needs (`12` invariant: one line, one statement). */
export interface TokenizerState {
  section: Section;
}

const reserved: ReadonlySet<string> = new Set(reservedWords);
const units: ReadonlySet<string> = new Set(unitSymbols);
const longestUnit = unitSymbols.reduce((max, s) => Math.max(max, s.length), 0);
const functions: ReadonlySet<string> = new Set(['min', 'max', 'sqrt', 'abs', 'clamp']);

const isDigit = (c: string): boolean => c >= '0' && c <= '9';
const isLetter = (c: string): boolean => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
const isWordStart = (c: string): boolean => isLetter(c) || c === '_';
const isWordChar = (c: string): boolean => isWordStart(c) || isDigit(c);

/** The state a document starts in. */
export function initialState(): TokenizerState {
  return { section: 'declarations' };
}

/**
 * Tokenizes one line, mirroring Core's `Lexer` rule for rule (`12` word classification 1--5, the
 * dot rule, maximal munch on the unit table) and then assigning roles by position (`12` statement
 * disambiguation: a reserved first word is that word's statement; otherwise `-` or `.` in second
 * place makes a connection, else a declaration). Updates the section when a header is seen.
 */
export function tokenizeLine(line: string, state: TokenizerState): LineToken[] {
  const lexical = lexLine(line);
  const tokens = assignRoles(lexical, state);

  const first = tokens.find((t) => t.kind !== 'Comment');
  if (first?.kind === 'Keyword') {
    if (first.text === 'connections') {
      state.section = 'connections';
    } else if (first.text === 'schedule') {
      state.section = 'schedule';
    } else if (first.text === 'curve') {
      state.section = 'curve';
    } else if (first.text === 'circuit') {
      state.section = 'declarations';
    }
  }

  return tokens;
}

/** A lexical token as Core's lexer would cut it, before any role is assigned. */
export interface Lexed {
  readonly from: number;
  readonly to: number;
  readonly kind: LineToken['kind'];
  readonly text: string;
  /** For a quantity, where its unit starts. */
  readonly unitFrom?: number;
  /** For a quantity, where its number ends; whitespace may lie between it and the unit (rule 5). */
  readonly numberTo?: number;
}

/** Cuts one line into Core's lexical tokens; the comment, if any, is the last. */
export function lexLine(line: string): Lexed[] {
  const out: Lexed[] = [];
  let i = 0;
  const n = line.length;
  const at = (k: number): string => (k < n ? line.charAt(k) : '');

  const matchUnit = (start: number, rejectBeforeEquals: boolean): number => {
    const available = Math.min(longestUnit, n - start);
    for (let length = available; length >= 1; length--) {
      const end = start + length;
      if (end < n && isWordChar(at(end))) {
        continue;
      }
      // Rule 5's clause, extended by D-120 to a port's state: `30 in.t=` and `30 in[2]` are a
      // number and a parameter name, not thirty inches.
      if (
        rejectBeforeEquals &&
        end < n &&
        (at(end) === '=' || at(end) === '[' || (at(end) === '.' && isWordStart(at(end + 1))))
      ) {
        continue;
      }
      if (units.has(line.slice(start, end))) {
        return length;
      }
    }
    return 0;
  };

  while (i < n) {
    const c = at(i);

    if (c === ' ' || c === '\t') {
      i++;
      continue;
    }

    if (c === '#') {
      out.push({ from: i, to: n, kind: 'Comment', text: line.slice(i) });
      break;
    }

    if (c === '"') {
      let j = i + 1;
      while (j < n && at(j) !== '"') {
        j++;
      }
      const to = j < n ? j + 1 : n;
      out.push({ from: i, to, kind: 'StringLiteral', text: line.slice(i, to) });
      i = to;
      continue;
    }

    if (isDigit(c)) {
      const start = i;
      // The number body: digits, a '.' only when a digit follows it, an exponent only with digits.
      while (i < n && isDigit(at(i))) {
        i++;
      }
      if (i + 1 < n && at(i) === '.' && isDigit(at(i + 1))) {
        i++;
        while (i < n && isDigit(at(i))) {
          i++;
        }
      }
      if (i < n && (at(i) === 'e' || at(i) === 'E')) {
        const afterSign = at(i + 1) === '+' || at(i + 1) === '-' ? i + 2 : i + 1;
        if (afterSign < n && isDigit(at(afterSign))) {
          i = afterSign;
          while (i < n && isDigit(at(i))) {
            i++;
          }
        }
      }
      const numberEnd = i;
      const numberText = line.slice(start, numberEnd);

      // Rule 3: the unit attached to the number.
      const attached = matchUnit(numberEnd, false);
      if (attached > 0) {
        i = numberEnd + attached;
        out.push({
          from: start,
          to: i,
          kind: 'QuantityLiteral',
          text: line.slice(start, i),
          unitFrom: numberEnd,
          numberTo: numberEnd,
        });
        continue;
      }

      // Rule 4: word characters that are not a unit make the whole run a name (3WV).
      if (numberEnd < n && isWordChar(at(numberEnd)) && [...numberText].every(isWordChar)) {
        while (i < n && isWordChar(at(i))) {
          i++;
        }
        out.push({ from: start, to: i, kind: 'Identifier', text: line.slice(start, i) });
        continue;
      }

      // Rule 5: a unit separated by horizontal whitespace, unless '=' follows it.
      let unitStart = numberEnd;
      while (unitStart < n && (at(unitStart) === ' ' || at(unitStart) === '\t')) {
        unitStart++;
      }
      if (unitStart > numberEnd) {
        const spaced = matchUnit(unitStart, true);
        if (spaced > 0) {
          i = unitStart + spaced;
          out.push({
            from: start,
            to: i,
            kind: 'QuantityLiteral',
            text: line.slice(start, i),
            unitFrom: unitStart,
            numberTo: numberEnd,
          });
          continue;
        }
      }

      i = numberEnd;
      out.push({ from: start, to: numberEnd, kind: 'NumberLiteral', text: numberText });
      continue;
    }

    if (isWordStart(c)) {
      const start = i;
      while (i < n && isWordChar(at(i))) {
        i++;
      }
      const text = line.slice(start, i);
      out.push({ from: start, to: i, kind: reserved.has(text) ? 'Keyword' : 'Identifier', text });
      continue;
    }

    if (c === '.' && at(i + 1) === '.') {
      out.push({ from: i, to: i + 2, kind: 'Punctuation', text: '..' });
      i += 2;
      continue;
    }

    const punctuation = '=-+*/.,()@:[]';
    if (punctuation.includes(c)) {
      out.push({ from: i, to: i + 1, kind: 'Punctuation', text: c });
    } else {
      out.push({ from: i, to: i + 1, kind: 'Unknown', text: c });
    }
    i++;
  }

  return out;
}

function assignRoles(lexed: readonly Lexed[], state: TokenizerState): LineToken[] {
  const out: LineToken[] = [];
  const significant = lexed.filter((t) => t.kind !== 'Comment');
  const first = significant[0];
  const second = significant[1];

  const directive = first?.kind === 'Keyword';
  const parameterNames = parameterNameTokens(significant);
  const connection =
    !directive &&
    state.section !== 'schedule' &&
    second?.kind === 'Punctuation' &&
    (second.text === '-' || second.text === '.');
  const declaration =
    !directive && !connection && state.section !== 'schedule' && state.section !== 'curve';

  let index = 0;
  for (const token of lexed) {
    if (token.kind === 'Comment') {
      out.push({ ...token, role: 'comment' });
      continue;
    }

    const previous = significant[index - 1];
    const next = significant[index + 1];
    index++;

    let role: TokenRole;
    switch (token.kind) {
      case 'Keyword':
        // A reserved word may stand in kind position (D-64): `S1 inlet t=5` declares a kind spelled with a keyword.
        role = declaration && index === 2 ? 'kind' : 'keyword';
        break;
      case 'NumberLiteral':
        role = 'number';
        break;
      case 'QuantityLiteral': {
        const unitFrom = token.unitFrom ?? token.to;
        const numberTo = token.numberTo ?? unitFrom;
        out.push({
          from: token.from,
          to: numberTo,
          role: 'number',
          kind: 'QuantityLiteral',
          text: token.text.slice(0, numberTo - token.from),
        });
        out.push({
          from: unitFrom,
          to: token.to,
          role: 'unit',
          kind: 'QuantityLiteral',
          text: token.text.slice(unitFrom - token.from),
        });
        continue;
      }
      case 'StringLiteral':
        role = 'string';
        break;
      case 'Punctuation':
        role = 'operator';
        break;
      case 'Unknown':
        role = 'unknown';
        break;
      case 'Identifier': {
        const inName = parameterNames.has(index - 1);
        const afterDot = previous?.kind === 'Punctuation' && previous.text === '.';
        const beforeEquals = next?.kind === 'Punctuation' && next.text === '=';
        const beforeParen = next?.kind === 'Punctuation' && next.text === '(';
        const named =
          directive &&
          index === 2 &&
          (first?.text === 'let' || first?.text === 'circuit' || first?.text === 'project');
        if (inName) {
          // Every word of `in[2].t=` is the parameter's name (D-120), the port included.
          role = 'parameter';
        } else if (afterDot) {
          role = connection ? 'port' : 'reference';
        } else if (named) {
          role = 'declaration';
        } else if (beforeEquals) {
          role = 'parameter';
        } else if (beforeParen && functions.has(token.text)) {
          role = 'function';
        } else if (declaration && index === 1) {
          role = 'declaration';
        } else if (declaration && index === 2) {
          role = 'kind';
        } else if (
          connection ||
          (directive &&
            (first?.text === 'inlet' || first?.text === 'outlet' || first?.text === 'control'))
        ) {
          role = 'name';
        } else {
          role = 'word';
        }
        break;
      }
    }

    out.push({ from: token.from, to: token.to, role, kind: token.kind, text: token.text });
  }

  return out;
}

/**
 * The indices (into the significant tokens) of every identifier that is part of a name before `=`:
 * `power` in `power=30`, and `in` and `t` in `in.t=20` or `in[2].t=85`. The name is whatever runs
 * back from the `=` without a gap, over words, dots, brackets and the index between them -- the
 * same adjacency the parser demands (`12`, D-120).
 */
function parameterNameTokens(significant: readonly Lexed[]): Set<number> {
  const names = new Set<number>();
  for (let k = 0; k < significant.length; k++) {
    const token = significant[k];
    if (token?.kind !== 'Punctuation' || token.text !== '=') {
      continue;
    }
    let end = token.from;
    for (let j = k - 1; j >= 0; j--) {
      const part = significant[j];
      if (part === undefined || part.to !== end) {
        break;
      }
      const joins =
        part.kind === 'Identifier' ||
        part.kind === 'NumberLiteral' ||
        (part.kind === 'Punctuation' &&
          (part.text === '.' || part.text === '[' || part.text === ']'));
      if (!joins) {
        break;
      }
      if (part.kind === 'Identifier') {
        names.add(j);
      }
      end = part.from;
    }
  }
  return names;
}

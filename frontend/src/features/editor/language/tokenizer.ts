import { eventWords, statementWords, unitSymbols } from './lexicon.generated.ts';

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
    | 'Identifier'
    | 'NumberLiteral'
    | 'QuantityLiteral'
    | 'DateLiteral'
    | 'StringLiteral'
    | 'Comment'
    | 'Punctuation'
    | 'Unknown';
  readonly text: string;
}

/** A block a line may sit in (`19` §Lines, blocks and names): what its head opened. */
export type BlockKind = 'project' | 'circuit' | 'run' | 'style' | 'declaration' | 'curve';

/** An open block: its kind, how deep its head is indented, and for a declaration its name and written kind. */
export interface OpenBlock {
  readonly kind: BlockKind;
  readonly indent: number;
  readonly name?: string;
  readonly writtenKind?: string;
}

/**
 * The state carried from line to line: the blocks open at the end of the last line read. A block
 * holds every following line indented deeper than its head (`19`), so the stack is all the grammar
 * needs; blank and comment lines leave it alone.
 */
export interface TokenizerState {
  blocks: OpenBlock[];
}

const statements: ReadonlySet<string> = new Set(statementWords);
const events: ReadonlySet<string> = new Set(eventWords);
const units: ReadonlySet<string> = new Set(unitSymbols);
const longestUnit = unitSymbols.reduce((max, s) => Math.max(max, s.length), 0);
const functions: ReadonlySet<string> = new Set(['min', 'max', 'sqrt', 'abs', 'clamp']);

const isDigit = (c: string): boolean => c >= '0' && c <= '9';
const isLetter = (c: string): boolean => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
const isWordStart = (c: string): boolean => isLetter(c) || c === '_';
const isWordChar = (c: string): boolean => isWordStart(c) || isDigit(c);

/** The state a document starts in: no block open. */
export function initialState(): TokenizerState {
  return { blocks: [] };
}

/** A copy of a state that the next line may change without changing this one. */
export function copyState(state: TokenizerState): TokenizerState {
  return { blocks: [...state.blocks] };
}

/** The block a line indented `indent` deep sits in, after closing every block it is not deeper than. */
export function enclosing(state: TokenizerState, indent: number): OpenBlock | null {
  while (state.blocks.length > 0 && (state.blocks[state.blocks.length - 1]?.indent ?? 0) >= indent) {
    state.blocks.pop();
  }
  return state.blocks[state.blocks.length - 1] ?? null;
}

/** How deep a line is indented, in characters, as Core counts it (`19`). */
export function indentOf(line: string): number {
  let i = 0;
  while (i < line.length && (line.charAt(i) === ' ' || line.charAt(i) === '\t')) {
    i++;
  }
  return i;
}

/**
 * Tokenizes one line, mirroring Core's `Lexer` rule for rule (maximal munch on the unit table, `3WV`
 * as a name, `30 kW` as one quantity, a date as one token, no reserved words) and then assigning
 * roles the way `LineParser.ClassifyLanguage2` reads a line: a statement word at the start names its
 * statement; otherwise the qualified name the line starts with is followed by `=` for a setting, `-`
 * for a connection, and anything else for a declaration. Updates the open blocks.
 */
export function tokenizeLine(line: string, state: TokenizerState): LineToken[] {
  const lexed = lexLine(line);
  const significant = lexed.filter((t) => t.kind !== 'Comment');
  if (significant.length === 0) {
    return lexed.map((t) => ({ ...t, role: 'comment' as const }));
  }

  const indent = indentOf(line);
  const block = enclosing(state, indent);
  const reading = classify(significant, block);
  const roles = assignRoles(significant, reading);
  if (reading.opens !== null) {
    state.blocks.push({ ...reading.opens, indent });
  }

  const out: LineToken[] = [];
  let index = 0;
  for (const token of lexed) {
    if (token.kind === 'Comment') {
      out.push({ ...token, role: 'comment' });
      continue;
    }
    const role = roles[index++] ?? 'word';
    if (token.kind === 'QuantityLiteral') {
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
    out.push({ from: token.from, to: token.to, role, kind: token.kind, text: token.text });
  }
  return out;
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
  const digits = (k: number, count: number): boolean => {
    if (k + count > n) {
      return false;
    }
    for (let j = k; j < k + count; j++) {
      if (!isDigit(at(j))) {
        return false;
      }
    }
    return true;
  };
  const clockEnd = (k: number): number => {
    if (!(digits(k, 2) && at(k + 2) === ':' && digits(k + 3, 2))) {
      return 0;
    }
    const end = k + 5;
    return at(end) === ':' && digits(end + 1, 2) ? end + 3 : end;
  };
  // `Lexer.ScanDate`: yyyy-MM-dd with an optional clock time after spaces or a `T`, or a clock time alone;
  // neither may run on into a word character.
  const dateEnd = (k: number): number => {
    if (digits(k, 4) && at(k + 4) === '-' && digits(k + 5, 2) && at(k + 7) === '-' && digits(k + 8, 2)) {
      let position = k + 10;
      let time = position;
      if (at(time) === 'T') {
        time++;
      } else {
        while (time < n && (at(time) === ' ' || at(time) === '\t')) {
          time++;
        }
      }
      const clock = time > position ? clockEnd(time) : 0;
      if (clock > 0) {
        position = clock;
      }
      return position < n && isWordChar(at(position)) ? 0 : position;
    }
    const end = clockEnd(k);
    return end > 0 && !(end < n && isWordChar(at(end))) ? end : 0;
  };
  const equalsAfterSpaces = (k: number): boolean => {
    while (k < n && (at(k) === ' ' || at(k) === '\t')) {
      k++;
    }
    return at(k) === '=';
  };

  const matchUnit = (start: number, rejectBeforeEquals: boolean): number => {
    const available = Math.min(longestUnit, n - start);
    for (let length = available; length >= 1; length--) {
      const end = start + length;
      if (end < n && isWordChar(at(end))) {
        continue;
      }
      // Rule 5's clause, looking past spaces since language 2 lets `=` stand apart (`19`): `flow = 5 h = 2000`
      // is five and a parameter named h; `30 in.t=` and `30 in[2]` are a number and a name.
      if (
        rejectBeforeEquals &&
        end < n &&
        (at(end) === '=' ||
          at(end) === '[' ||
          (at(end) === '.' && isWordStart(at(end + 1))) ||
          equalsAfterSpaces(end))
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

    // A byte-order mark opens a file saved by some Windows editors; it is whitespace (`18`).
    if (c === ' ' || c === '\t' || (c === '﻿' && i === 0)) {
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
      const date = dateEnd(i);
      if (date > 0) {
        out.push({ from: start, to: date, kind: 'DateLiteral', text: line.slice(start, date) });
        i = date;
        continue;
      }

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
      // The lexer reserves nothing (`19`): a statement word is known by where it stands.
      out.push({ from: start, to: i, kind: 'Identifier', text: line.slice(start, i) });
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

// ---- lines ------------------------------------------------------------------------------------------

/** What a line is (`19` §Statements), read from its tokens and the block it sits in. */
export type LineKind =
  | 'version'
  | 'head'
  | 'let'
  | 'curve-head'
  | 'style-head'
  | 'event'
  | 'row'
  | 'setting'
  | 'connection'
  | 'declaration';

/** A line's reading: what it is, and the block it opens, if any. */
export interface LineReading {
  readonly kind: LineKind;
  readonly opens: Omit<OpenBlock, 'indent'> | null;
}

const isPunct = (token: Lexed | undefined, text: string): boolean =>
  token?.kind === 'Punctuation' && token.text === text;

/** Where the qualified name starting at `start` ends: `HX1.secondary.in`, `in[2].t`. */
export function qualifiedEnd(tokens: readonly Lexed[], start: number): number {
  let k = start;
  if (tokens[k]?.kind !== 'Identifier') {
    return k;
  }
  k++;
  for (;;) {
    if (isPunct(tokens[k], '[') && tokens[k + 1]?.kind === 'NumberLiteral' && isPunct(tokens[k + 2], ']')) {
      k += 3;
      continue;
    }
    if (isPunct(tokens[k], '.') && tokens[k + 1]?.kind === 'Identifier') {
      k += 2;
      continue;
    }
    return k;
  }
}

/** Reads a line as `LineParser.ClassifyLanguage2` does, given the block it sits in. */
export function classify(tokens: readonly Lexed[], block: OpenBlock | null): LineReading {
  const first = tokens[0];
  const second = tokens[1];
  const none = (kind: LineKind): LineReading => ({ kind, opens: null });

  if (block?.kind === 'curve') {
    return none('row');
  }
  if (first === undefined) {
    return none('setting');
  }
  if (
    first.kind === 'NumberLiteral' ||
    first.kind === 'DateLiteral' ||
    (first.kind === 'QuantityLiteral' && second?.kind !== 'Identifier') ||
    isPunct(first, '-')
  ) {
    return none('row');
  }

  if (first.kind === 'Identifier' && statements.has(first.text) && !isPunct(second, '=')) {
    switch (first.text) {
      case 'fluidscript':
        return none('version');
      case 'project':
      case 'circuit':
      case 'run':
        return { kind: 'head', opens: { kind: first.text } };
      case 'let':
        return none('let');
      case 'curve':
        return { kind: 'curve-head', opens: { kind: 'curve' } };
    }
  }
  if (first.kind === 'Identifier' && first.text === 'style' && (second === undefined || isPunct(second, ':'))) {
    return { kind: 'style-head', opens: { kind: 'style' } };
  }
  if (first.kind === 'Identifier' && events.has(first.text) && block?.kind === 'run' && second !== undefined) {
    return none('event');
  }

  const after = tokens[qualifiedEnd(tokens, 0)];
  if (isPunct(after, '=')) {
    return none('setting');
  }
  if (isPunct(after, '-')) {
    return none('connection');
  }
  const last = tokens[tokens.length - 1];
  const opens =
    isPunct(last, ':') && first.kind === 'Identifier'
      ? {
          kind: 'declaration' as const,
          name: first.text,
          ...(second?.kind === 'Identifier' ? { writtenKind: second.text } : {}),
        }
      : null;
  return { kind: 'declaration', opens };
}

/**
 * The indices of every identifier that is part of a name before `=`: `power` in `power = 30`, and
 * `primary`, `in` and `t` in `primary.in.t = 45 C`. The name is whatever runs back from the `=`,
 * spaces allowed before it, over words, dots, brackets and the index between them, which themselves
 * touch -- the adjacency the parser demands (`12`, `D-120`).
 */
export function parameterNameTokens(tokens: readonly Lexed[]): Set<number> {
  const names = new Set<number>();
  for (let k = 0; k < tokens.length; k++) {
    if (!isPunct(tokens[k], '=')) {
      continue;
    }
    let end: number | null = null;
    for (let j = k - 1; j >= 0; j--) {
      const part = tokens[j];
      if (part === undefined || (end !== null && part.to !== end)) {
        break;
      }
      const joins =
        part.kind === 'Identifier' ||
        (end !== null && part.kind === 'NumberLiteral') ||
        (part.kind === 'Punctuation' && (part.text === '.' || part.text === '[' || part.text === ']'));
      if (!joins || (end === null && part.kind === 'Punctuation' && part.text !== ']')) {
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

function assignRoles(tokens: readonly Lexed[], reading: LineReading): TokenRole[] {
  const names = parameterNameTokens(tokens);
  const roles: TokenRole[] = [];

  // A value's words: a function before its parenthesis, a property after a dot, a name otherwise.
  const valueRole = (k: number): TokenRole => {
    const previous = tokens[k - 1];
    const next = tokens[k + 1];
    if (isPunct(next, '(') && functions.has(tokens[k]?.text ?? '')) {
      return 'function';
    }
    return isPunct(previous, '.') ? 'reference' : 'word';
  };

  // Where a connection's chain ends: the pipe's length, DN and settings follow it (`19` §Connections).
  let chainEnd = -1;
  if (reading.kind === 'connection') {
    let k = qualifiedEnd(tokens, 0);
    while (isPunct(tokens[k], '-') && tokens[k + 1]?.kind === 'Identifier') {
      k = qualifiedEnd(tokens, k + 1);
    }
    chainEnd = k;
  }

  for (let k = 0; k < tokens.length; k++) {
    const token = tokens[k]!;
    const previous = tokens[k - 1];
    let role: TokenRole;

    switch (token.kind) {
      case 'NumberLiteral':
      case 'QuantityLiteral':
      case 'DateLiteral':
        role = 'number';
        break;
      case 'StringLiteral':
        role = 'string';
        break;
      case 'Punctuation':
        role = 'operator';
        break;
      case 'Unknown':
        role = 'unknown';
        break;
      case 'Comment':
        role = 'comment';
        break;
      case 'Identifier':
        if (k === 1 && (reading.kind === 'let' || reading.kind === 'curve-head')) {
          role = 'declaration';
        } else if (names.has(k)) {
          // Every word of `primary.in.t =` is the setting's name (D-120).
          role = 'parameter';
        } else if (k === 0 && reading.kind !== 'setting' && reading.kind !== 'connection' && reading.kind !== 'declaration') {
          role = 'keyword';
        } else if (reading.kind === 'curve-head' && k > 1 && !isPunct(previous, ':') && !isPunct(tokens[k + 1], '=')) {
          role = 'keyword';
        } else if (reading.kind === 'declaration' && k === 0) {
          role = 'declaration';
        } else if (reading.kind === 'declaration' && k === 1) {
          role = 'kind';
        } else if (reading.kind === 'connection' && k < chainEnd) {
          role = isPunct(previous, '.') ? 'port' : 'name';
        } else if (reading.kind === 'event' && isPunct(tokens[k + 1], '.') && !names.has(k)) {
          role = 'name';
        } else {
          role = valueRole(k);
        }
        break;
    }
    roles.push(role);
  }
  return roles;
}

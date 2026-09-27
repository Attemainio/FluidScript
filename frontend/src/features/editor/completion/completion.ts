import type {
  Kind,
  Metadata,
  ModelContract,
  ParameterMeta,
  PropertyMeta,
  Setting,
} from '../../../api/types.ts';
import { ambiguityMargin, eventWords, resolveThreshold, statementWords } from '../language/lexicon.generated.ts';
import {
  enclosing,
  indentOf,
  initialState,
  lexLine,
  qualifiedEnd,
  tokenizeLine,
  type Lexed,
  type OpenBlock,
  type TokenizerState,
} from '../language/tokenizer.ts';
import { normalize, score } from './similarity.ts';

/** One offered completion, editor-neutral; the CodeMirror source maps it. */
export interface Item {
  /** What is shown and matched. */
  readonly label: string;
  /** What is inserted, when it differs from the label (a kind reached through an alias, a template). */
  readonly insert?: string;
  /** `power · Power · kW · typically 1…10000` (`52`). */
  readonly detail?: string;
  /** A longer explanation: the parameter's basis, the alias that matched. */
  readonly info?: string;
  readonly type:
    | 'keyword'
    | 'kind'
    | 'parameter'
    | 'setting'
    | 'let'
    | 'reference'
    | 'unit'
    | 'value'
    | 'name'
    | 'port'
    | 'property'
    | 'template'
    | 'target';
  /** Dimmed: an unnamed dimension, a deferred value. */
  readonly dimmed?: boolean;
  /** Shown but preselected by nothing: an ambiguous match (`52`). */
  readonly ambiguous?: boolean;
  /** The higher, the earlier. */
  readonly rank: number;
}

/** What completion offered, and where the typed prefix begins. */
export interface Completion {
  readonly from: number;
  readonly items: readonly Item[];
  /** The context the items came from; a test reads it. */
  readonly context: ContextKind;
}

/** The positions of `52`'s table. */
export type ContextKind =
  | 'none'
  | 'statement'
  | 'setting'
  | 'kind'
  | 'parameter'
  | 'value'
  | 'connection-name'
  | 'port'
  | 'property'
  | 'target'
  | 'let-value'
  | 'driver';

/** What completion needs to know: the language and the last compile. */
export interface Sources {
  readonly metadata: Metadata | null;
  readonly model: ModelContract | null;
}

/** Where the cursor is: the whole document and the offset. */
export interface Position {
  readonly doc: string;
  readonly offset: number;
}

/** A value's filter: the dimension it must have and the words it may be, or nothing known. */
interface Want {
  readonly dimension: string | null;
  readonly words: readonly string[];
}

/** Everything a position is read against. */
interface Here {
  readonly metadata: Metadata;
  readonly sources: Sources;
  readonly doc: string;
  readonly lineStart: number;
  readonly block: OpenBlock | null;
  readonly tokens: readonly Lexed[];
}

const isPunct = (token: Lexed | undefined, text: string): boolean =>
  token?.kind === 'Punctuation' && token.text === text;

const statements: ReadonlySet<string> = new Set(statementWords);

/**
 * Completion at a position (`52`'s table), read as the parser reads the line (`19`): the block the
 * line sits in, then what the line's first name is followed by. Everything offered comes from
 * `/metadata`, the model, or a name the document itself writes (invariant 5); with no metadata the
 * list is empty and typing is unaffected (`52` error cases).
 */
export function complete(position: Position, sources: Sources): Completion {
  const { lineStart, line, column, state } = locate(position);
  const before = line.slice(0, column);
  const wordStart = wordStartAt(before);
  const prefix = before.slice(wordStart);
  const from = lineStart + wordStart;
  const metadata = sources.metadata;

  const none = (context: ContextKind = 'none'): Completion => ({ from, items: [], context });
  const lexed = lexLine(before);
  if (metadata === null || lexed.some((t) => t.kind === 'Comment' || t.kind === 'StringLiteral' && t.to > wordStart)) {
    return none();
  }

  const tokens = lexed.filter((t) => t.to <= wordStart);
  const block = enclosing(state, indentOf(before));
  const here: Here = { metadata, sources, doc: position.doc, lineStart, block, tokens };
  const at = (context: ContextKind, items: Item[]): Completion => ({ from, items, context });

  if (block?.kind === 'curve') {
    return none();
  }

  const first = tokens[0];
  const last = tokens[tokens.length - 1];

  // ---- the start of a line: what a line in this block may begin with --------------------------------
  if (first === undefined) {
    const start = lineStartItems(here);
    return at(start.context, start.items);
  }

  const opener = first.kind === 'Identifier' && statements.has(first.text) && !isPunct(tokens[1], '=');
  const event = first.kind === 'Identifier' && eventWords.includes(first.text) && block?.kind === 'run';

  // ---- after a dot: a port, a port's state, a property or an event's target -------------------------
  if (isPunct(last, '.')) {
    return afterDot(here, event);
  }

  // A curve's driver, after `curve NAME:`: a let, or the clock.
  if (opener && first.text === 'curve') {
    return isPunct(last, ':') ? at('driver', drivers(here)) : none();
  }
  if (opener && first.text === 'let') {
    return isPunct(last, '=') || isOperator(last) ? at('let-value', values(here, null)) : none();
  }
  if (opener) {
    return none();
  }

  const equalsAt = lastIndex(tokens, (t) => isPunct(t, '='));
  const chainEnd = connectionEnd(tokens);

  // ---- a connection: names after each `-`, the pipe's settings after the chain -----------------------
  if (chainEnd !== null) {
    if (isPunct(last, '-') && tokens.length <= chainEnd) {
      return at('connection-name', componentNames(here, true));
    }
    const pipe = kindByKeyword(metadata, 'pipe');
    if (equalsAt >= 0 && (isPunct(last, '=') || isOperator(last))) {
      return at('value', values(here, wantOf(parameterOf(pipe, nameBefore(tokens, equalsAt)))));
    }
    if (tokens.length > chainEnd || !isPunct(last, '-')) {
      return at('parameter', parameters(pipe, writtenOnLine(tokens)));
    }
    return none();
  }

  // ---- an event: its time, then its target, then its value --------------------------------------------
  if (event) {
    if (isPunct(last, '=')) {
      return at('value', values(here, wantOf(targetParameter(here, nameBefore(tokens, equalsAt)))));
    }
    if (equalsAt < 0 && tokens.length >= 2 && isTime(last)) {
      return at('target', componentNames(here, false));
    }
    return none();
  }

  // ---- a setting's value ----------------------------------------------------------------------------
  if (isPunct(last, '=') || (equalsAt >= 0 && isOperator(last))) {
    return at('value', valueAfter(here, nameBefore(tokens, equalsAt), equalsAt));
  }

  // ---- a declaration: its kind, then its parameters -------------------------------------------------
  const settingLine = isPunct(tokens[qualifiedEnd(tokens, 0)], '=');
  if (!settingLine && tokens.length === 1 && first.kind === 'Identifier') {
    return block === null || block.kind === 'circuit' ? at('kind', kinds(metadata, prefix)) : none();
  }
  if (!settingLine && tokens[1]?.kind === 'Identifier') {
    if (isPunct(last, ':')) {
      return none();
    }
    const kind = resolveKind(metadata, tokens[1].text);
    return at('parameter', declarationItems(here, kind, first.text, writtenOnLine(tokens)));
  }

  // ---- more settings on a setting line: `name = value   name = value` -------------------------------
  if (settingLine && equalsAt >= 0 && last !== undefined && !isPunct(last, '=')) {
    return at('parameter', blockItems(here, writtenOnLine(tokens)).filter((i) => i.type !== 'name'));
  }

  return none();
}

// ---- positions ---------------------------------------------------------------------------------------

function locate(position: Position): {
  lineStart: number;
  line: string;
  column: number;
  state: TokenizerState;
} {
  const doc = position.doc;
  const lineStart = doc.lastIndexOf('\n', position.offset - 1) + 1;
  const lineEnd = doc.indexOf('\n', position.offset);
  const line = doc.slice(lineStart, lineEnd < 0 ? doc.length : lineEnd).replace(/\r$/, '');
  const state = initialState();
  let at = 0;
  while (at < lineStart) {
    const end = doc.indexOf('\n', at);
    const stop = end < 0 ? doc.length : end;
    tokenizeLine(doc.slice(at, stop).replace(/\r$/, ''), state);
    at = stop + 1;
  }
  return { lineStart, line, column: position.offset - lineStart, state };
}

function wordStartAt(before: string): number {
  let i = before.length;
  while (i > 0 && /[A-Za-z0-9_]/.test(before.charAt(i - 1))) {
    i--;
  }
  return i;
}

function lastIndex(tokens: readonly Lexed[], test: (token: Lexed) => boolean): number {
  for (let i = tokens.length - 1; i >= 0; i--) {
    if (test(tokens[i]!)) {
      return i;
    }
  }
  return -1;
}

function isOperator(token: Lexed | undefined): boolean {
  return token?.kind === 'Punctuation' && '+-*/(,['.includes(token.text);
}

function isTime(token: Lexed | undefined): boolean {
  return (
    token?.kind === 'QuantityLiteral' ||
    token?.kind === 'NumberLiteral' ||
    token?.kind === 'DateLiteral'
  );
}

/** Where a connection's chain ends, or null when the line is not one (`19`: a name, then `-`). */
function connectionEnd(tokens: readonly Lexed[]): number | null {
  if (tokens[0]?.kind !== 'Identifier' || statements.has(tokens[0].text)) {
    return null;
  }
  let k = qualifiedEnd(tokens, 0);
  if (!isPunct(tokens[k], '-')) {
    return null;
  }
  while (isPunct(tokens[k], '-')) {
    if (tokens[k + 1]?.kind !== 'Identifier') {
      return k + 1;
    }
    k = qualifiedEnd(tokens, k + 1);
  }
  return k;
}

/**
 * The name whose last token sits just before `tokens[equals]`: `power`, or `secondary.in.t` -- whatever
 * runs back over words, dots, brackets and the index between them, which touch (`12`, D-120).
 */
function nameBefore(tokens: readonly Lexed[], equals: number): string {
  let end = equals - 1;
  const tail = tokens[end];
  if (tail === undefined || (tail.kind !== 'Identifier' && !isPunct(tail, ']'))) {
    return '';
  }
  let start = end;
  let from = tail.from;
  for (let j = end - 1; j >= 0; j--) {
    const part = tokens[j];
    if (part === undefined || part.to !== from) {
      break;
    }
    const joins =
      part.kind === 'Identifier' ||
      part.kind === 'NumberLiteral' ||
      (part.kind === 'Punctuation' && (part.text === '.' || part.text === '[' || part.text === ']'));
    if (!joins) {
      break;
    }
    start = j;
    from = part.from;
  }
  end = equals;
  return tokens
    .slice(start, end)
    .map((t) => t.text)
    .join('');
}

/** The names already written before an `=` on the line, normalised. */
function writtenOnLine(tokens: readonly Lexed[]): Set<string> {
  const written = new Set<string>();
  tokens.forEach((token, index) => {
    if (isPunct(token, '=')) {
      const name = nameBefore(tokens, index);
      if (name.length > 0) {
        written.add(normalize(name));
      }
    }
  });
  return written;
}

/**
 * The names written before an `=` on the lines of the block the cursor sits in, above the cursor: a
 * block's settings and a declaration block's parameters are each written once.
 */
function writtenInBlock(here: Here): Set<string> {
  const written = new Set<string>();
  const block = here.block;
  if (block === null) {
    return written;
  }
  let end = here.lineStart - 1;
  while (end > 0) {
    const start = here.doc.lastIndexOf('\n', end - 1) + 1;
    const line = here.doc.slice(start, end).replace(/\r$/, '');
    const tokens = lexLine(line).filter((t) => t.kind !== 'Comment');
    if (tokens.length > 0) {
      if (indentOf(line) <= block.indent) {
        break;
      }
      writtenOnLine(tokens).forEach((name) => written.add(name));
    }
    end = start - 1;
  }
  return written;
}

/** A controller's type as its block or its line states it; `PI` when absent (`19`). */
function controllerType(here: Here): string {
  const own = /\btype\s*=\s*(\w+)/.exec(here.tokens.map((t) => t.text).join(' '));
  if (own !== null) {
    return own[1] ?? 'PI';
  }
  const block = here.block;
  if (block === null) {
    return 'PI';
  }
  let end = here.lineStart - 1;
  while (end > 0) {
    const start = here.doc.lastIndexOf('\n', end - 1) + 1;
    const line = here.doc.slice(start, end);
    const match = /^\s*type\s*=\s*(\w+)/.exec(line);
    if (match !== null) {
      return match[1] ?? 'PI';
    }
    if (line.trim().length > 0 && !line.trim().startsWith('#') && indentOf(line) <= block.indent) {
      break;
    }
    end = start - 1;
  }
  return 'PI';
}

// ---- what a position offers ----------------------------------------------------------------------

/** A line's first word: the statement words at the top, a block's settings and names inside it. */
function lineStartItems(here: Here): { context: ContextKind; items: Item[] } {
  if (here.block === null) {
    let rank = 1000;
    return {
      context: 'statement',
      items: statementWords.map((word) => ({ label: word, type: 'keyword', rank: rank-- })),
    };
  }
  return { context: 'setting', items: blockItems(here, writtenInBlock(here)) };
}

/** What a line in the cursor's block may start with, less what is already written. */
function blockItems(here: Here, written: Set<string>): Item[] {
  const block = here.block;
  const metadata = here.metadata;
  switch (block?.kind) {
    case 'project':
    case 'circuit':
    case 'run':
    case 'style': {
      const items = settingItems(blockSettings(metadata, block.kind), written);
      if (block.kind === 'circuit') {
        items.push(...componentNames(here, true, 500));
      }
      if (block.kind === 'run') {
        let rank = 500;
        for (const word of eventWords) {
          items.push({ label: word, type: 'keyword', detail: word === 'at' ? 'a step' : 'a ramp', rank: rank-- });
        }
        for (const binding of here.sources.model?.bindings ?? []) {
          items.push({ label: binding.name, type: 'let', detail: 'an override for this run', rank: rank-- });
        }
        items.push(...componentNames(here, false, rank));
      }
      return items;
    }
    case 'declaration': {
      const kind = block.writtenKind === undefined ? null : resolveKind(metadata, block.writtenKind);
      return declarationItems(here, kind, block.name ?? '', written);
    }
    default:
      return [];
  }
}

/** A declaration's parameters: its kind's, or a controller's settings for its type (`D-168`). */
function declarationItems(here: Here, kind: Kind | null, name: string, written: Set<string>): Item[] {
  if (kind?.keyword === 'controller') {
    const type = controllerType(here);
    const settings = blockSettings(here.metadata, 'controller').filter(
      (s) => s.types.length === 0 || s.types.some((t) => normalize(t) === normalize(type)),
    );
    return settingItems(settings, written);
  }
  return parameters(kind, written, componentNamed(name, here.sources));
}

function blockSettings(metadata: Metadata, name: string): readonly Setting[] {
  return metadata.blocks.find((b) => b.name === name)?.settings ?? [];
}

function settingItems(settings: readonly Setting[], written: Set<string>): Item[] {
  let rank = 1000;
  const items: Item[] = [];
  for (const setting of settings) {
    if (written.has(normalize(setting.name)) || setting.aliases.some((a) => written.has(normalize(a)))) {
      continue;
    }
    items.push({
      label: setting.name,
      ...(setting.valueKind === 'block' ? { insert: `${setting.name}:` } : {}),
      type: 'setting',
      detail: setting.dimension === null ? setting.meaning : `${setting.dimension} · ${setting.meaning}`,
      rank: rank--,
    });
  }
  return items;
}

/** The value after `name =`, by what the name is where it stands. */
function valueAfter(here: Here, name: string, equalsAt: number): Item[] {
  const tokens = here.tokens;
  const block = here.block;
  const metadata = here.metadata;

  // On a declaration line, `NAME kind  param = …`: the kind's parameter.
  if (!isPunct(tokens[qualifiedEnd(tokens, 0)], '=') && tokens[1]?.kind === 'Identifier' && equalsAt >= 2) {
    const kind = resolveKind(metadata, tokens[1].text);
    return kind?.keyword === 'controller'
      ? settingValues(here, blockSettings(metadata, 'controller'), name)
      : values(here, wantOf(parameterOf(kind, name)));
  }

  switch (block?.kind) {
    case 'declaration': {
      const kind = block.writtenKind === undefined ? null : resolveKind(metadata, block.writtenKind);
      return kind?.keyword === 'controller'
        ? settingValues(here, blockSettings(metadata, 'controller'), name)
        : values(here, wantOf(parameterOf(kind, name)));
    }
    case 'project':
    case 'circuit':
    case 'style':
      return settingValues(here, blockSettings(metadata, block.kind), name);
    case 'run': {
      if (findSetting(blockSettings(metadata, 'run'), name) !== null) {
        return settingValues(here, blockSettings(metadata, 'run'), name);
      }
      // An override: of a parameter, `RAD.power =`, or of a let, `outdoor =`.
      if (name.includes('.')) {
        return values(here, wantOf(targetParameter(here, name)));
      }
      const binding = here.sources.model?.bindings.find((b) => b.name === name);
      return values(here, binding === undefined ? null : { dimension: binding.dimension, words: [] }, true);
    }
    default:
      return values(here, null);
  }
}

function findSetting(settings: readonly Setting[], written: string): Setting | null {
  const normalized = normalize(written);
  return (
    settings.find(
      (s) => normalize(s.name) === normalized || s.aliases.some((a) => normalize(a) === normalized),
    ) ?? null
  );
}

/** A setting's value by what it is (`SettingValueKind`): its words, the script's own names, or a quantity. */
function settingValues(here: Here, settings: readonly Setting[], name: string): Item[] {
  const setting = findSetting(settings, name);
  if (setting === null) {
    return [];
  }
  let rank = 1000;
  const words = (list: readonly string[], detail?: string): Item[] =>
    list.map((word) => ({ label: word, type: 'value', ...(detail === undefined ? {} : { detail }), rank: rank-- }));
  const model = here.sources.model;

  switch (setting.valueKind) {
    case 'word':
    case 'substance':
    case 'circuitRole':
    case 'catalog':
      return words(setting.values);
    case 'case':
      return words(namesInDocument(here.doc, /^\s*cases\s*=\s*\[([^\]]*)\]/m), 'a case');
    case 'circuits':
      return (model?.circuits ?? []).map((c) => ({
        label: `"${c.name}"`,
        type: 'value',
        detail: 'a circuit',
        rank: rank--,
      }));
    case 'quantity':
      return values(here, setting.dimension === null ? null : { dimension: setting.dimension, words: [] });
    case 'actuator':
      return actuators(here);
    case 'measurement':
      return [...measurements(here), ...drivers(here)];
    case 'value':
      return [...values(here, null), ...curves(here)];
    case 'curve':
      return curves(here);
    default:
      return [];
  }
}

/** After a dot: which of the four a dotted name is depends on where it stands (`19`). */
function afterDot(here: Here, event: boolean): Completion {
  const tokens = here.tokens;
  const metadata = here.metadata;
  const dot = tokens.length - 1;
  let start = dot;
  while (start > 0) {
    const part = tokens[start - 1];
    const next = tokens[start];
    if (part === undefined || next === undefined || part.to !== next.from) {
      break;
    }
    if (part.kind !== 'Identifier' && part.kind !== 'NumberLiteral' && !isPunct(part, '.') && !isPunct(part, '[') && !isPunct(part, ']')) {
      break;
    }
    start--;
  }
  const owner = tokens[start];
  const path = tokens
    .slice(start + 1, dot)
    .map((t) => t.text)
    .join('')
    .replace(/^\./, '');
  const from = here.lineStart + (tokens[dot]?.to ?? 0);
  const at = (context: ContextKind, items: Item[]): Completion => ({ from, items, context });
  if (owner?.kind !== 'Identifier') {
    return at('none', []);
  }
  const previous = tokens[start - 1];
  const block = here.block;
  const valuePosition =
    tokens.slice(0, start).some((t) => isPunct(t, '=')) || isOperator(previous) && !isPunct(previous, '-');

  // In a value: a component's property, `HX1.dp`, or the rest of a property's spelling.
  if (valuePosition) {
    return at('property', properties(kindOfComponent(owner.text, here), path));
  }
  // An event's target, or a run's override of a parameter.
  if (event || (block?.kind === 'run' && start === 0)) {
    return at('target', targets(kindOfComponent(owner.text, here), path));
  }
  // A port's state on a declaration: `HX1 exchanger  secondary.in.` or in its block, `primary.out.`.
  const declarationLine = start >= 2 && tokens[1]?.kind === 'Identifier' && !isPunct(previous, '-');
  if (declarationLine || (block?.kind === 'declaration' && start === 0)) {
    const written = declarationLine ? tokens[1]!.text : block?.writtenKind;
    const kind = written === undefined ? null : resolveKind(metadata, written);
    const head = tokens
      .slice(start, dot)
      .map((t) => t.text)
      .join('');
    const items = kind === null ? [] : portQuantities(kind, head, writtenOnLine(tokens));
    return at(items.length > 0 ? 'parameter' : 'port', items.length > 0 ? items : kind === null ? [] : portPaths(kind, head));
  }
  // A connection's end: the component's ports, as a script spells them.
  return at('port', ports(kindOfComponent(owner.text, here), owner.text, here.sources, path));
}

// ---- kinds ------------------------------------------------------------------------------------------

function kindByKeyword(metadata: Metadata, keyword: string): Kind | null {
  return metadata.kinds.find((k) => k.keyword === keyword) ?? null;
}

/**
 * Resolves a written kind as the binder does since `D-170`: by its spelling, case and underscores
 * aside, or a curated alias. A merely similar spelling does not bind -- the binder reports it with
 * the near kind as the fix -- so it resolves to nothing here either.
 */
export function resolveKind(metadata: Metadata, written: string): Kind | null {
  const normalized = normalize(written);
  for (const kind of metadata.kinds) {
    if (
      normalize(kind.keyword) === normalized ||
      kind.aliases.some((a) => normalize(a) === normalized)
    ) {
      return kind;
    }
  }
  return null;
}

interface RankedKind {
  readonly kind: Kind;
  readonly score: number;
  readonly via: string | null;
  readonly tier: number;
}

function rankKinds(metadata: Metadata, written: string): RankedKind[] {
  const normalized = normalize(written);
  const ranked: RankedKind[] = [];
  for (const kind of metadata.kinds) {
    const keyword = normalize(kind.keyword);
    let best: RankedKind | null = null;
    const consider = (candidate: RankedKind): void => {
      if (
        best === null ||
        candidate.tier < best.tier ||
        (candidate.tier === best.tier && candidate.score > best.score)
      ) {
        best = candidate;
      }
    };
    if (keyword === normalized) {
      consider({ kind, score: 1, via: null, tier: 1 });
    } else if (normalized.length > 0 && keyword.startsWith(normalized)) {
      consider({ kind, score: 1 - (keyword.length - normalized.length) / 100, via: null, tier: 2 });
    }
    for (const alias of kind.aliases) {
      const a = normalize(alias);
      if (a === normalized || (normalized.length > 0 && a.startsWith(normalized))) {
        consider({ kind, score: a === normalized ? 1 : 0.99, via: alias, tier: 3 });
      }
    }
    if (normalized.length > 0) {
      const similarity = Math.max(
        score(normalized, keyword),
        ...kind.aliases.map((a) => score(normalized, normalize(a))),
      );
      // A near spelling is offered as the binder offers it, as the fix (`D-170`): it never binds as written.
      if (similarity >= resolveThreshold) {
        consider({ kind, score: similarity, via: null, tier: 4 });
      }
    }
    if (best !== null) {
      ranked.push(best);
    }
  }
  return ranked.sort(
    (x, y) =>
      x.tier - y.tier ||
      y.score - x.score ||
      x.kind.keyword.length - y.kind.keyword.length ||
      x.kind.keyword.localeCompare(y.kind.keyword),
  );
}

function kinds(metadata: Metadata, prefix: string): Item[] {
  if (prefix.length === 0) {
    return metadata.kinds.map((kind, index) => ({
      label: kind.keyword,
      type: 'kind',
      detail: describeKind(kind),
      rank: 1000 - index,
    }));
  }
  const ranked = rankKinds(metadata, prefix);
  // Ambiguity is shown, not resolved (52): two similarity matches within the margin are both listed and neither is preselected.
  const similar = ranked.filter((r) => r.tier === 4);
  const ambiguous =
    similar.length >= 2 &&
    (similar[0]?.score ?? 0) - (similar[1]?.score ?? 0) < ambiguityMargin &&
    ranked[0]?.tier === 4;
  return ranked.map((r, index) => ({
    label: r.kind.keyword,
    insert: r.kind.keyword,
    type: 'kind',
    detail:
      r.via !== null ? `via '${r.via}'` : r.tier === 4 ? `did you mean ${r.kind.keyword}?` : describeKind(r.kind),
    ...(ambiguous && index < 2 ? { ambiguous: true } : {}),
    rank: 1000 - index,
  }));
}

function describeKind(kind: Kind): string {
  const ports = kind.ports.map((p) => p.spelling).join(', ');
  return ports.length === 0 ? '' : `Ports: ${ports}`;
}

// ---- parameters -----------------------------------------------------------------------------------

function parameterOf(kind: Kind | null, written: string): ParameterMeta | null {
  if (kind === null || written.length === 0) {
    return null;
  }
  const normalized = normalize(written);
  const direct = kind.parameters.find(
    (p) => normalize(p.name) === normalized || p.aliases.some((a) => normalize(a) === normalized),
  );
  if (direct !== undefined) {
    return direct;
  }
  for (const family of kind.indexedParameters) {
    if (familyPattern(family.pattern).test(written)) {
      return family.element;
    }
  }
  return null;
}

function wantOf(parameter: ParameterMeta | null): Want | null {
  if (parameter === null) {
    return null;
  }
  return {
    dimension: parameter.dimension,
    words: parameter.valueKind === 'symbol' ? parameter.acceptedSymbols : [],
  };
}

/** Escapes a family pattern's brackets and dots and turns `{index}` into a capture. */
function familyPattern(pattern: string): RegExp {
  const escaped = pattern.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp('^' + escaped.replace('\\{index\\}', '(\\d+)') + '$');
}

/** A port spelled another way resolves to the spelling its parameters are named by: `primary.in` is `in` (`D-179`). */
function canonicalPort(kind: Kind, head: string): string {
  const port = kind.ports.find((p) => p.aliases.includes(head));
  return port?.spelling ?? head.replace('[1]', '');
}

/**
 * The quantities a port's state may be written with, after `in.` or `secondary.in.`: every parameter
 * of the kind named `{port}.{quantity}`, minus the ones the line already states (D-120, D-179).
 */
function portQuantities(kind: Kind, written: string, stated: Set<string>): Item[] {
  const head = canonicalPort(kind, written);
  const items: Item[] = [];
  let rank = 1000;
  for (const parameter of kind.parameters) {
    if (!parameter.name.startsWith(head + '.')) {
      continue;
    }
    const rest = parameter.name.slice(head.length + 1);
    if (rest.includes('.') || stated.has(normalize(parameter.name))) {
      continue;
    }
    items.push({
      label: rest,
      type: 'parameter',
      detail: describeParameter(parameter),
      info: omissionText(parameter),
      rank: rank--,
    });
  }
  for (const family of kind.indexedParameters) {
    const dot = family.pattern.lastIndexOf('.');
    if (dot < 0) {
      continue;
    }
    const match = familyPattern(family.pattern.slice(0, dot)).exec(written);
    if (match === null) {
      continue;
    }
    const name = family.pattern.replace('{index}', match[1] ?? '');
    if (!stated.has(normalize(name))) {
      items.push({
        label: family.pattern.slice(dot + 1),
        type: 'parameter',
        detail: describeParameter(family.element),
        rank: rank--,
      });
    }
  }
  return items;
}

/** After `secondary.` on a declaration: the rest of each port spelling that starts there. */
function portPaths(kind: Kind, head: string): Item[] {
  const seen = new Set<string>();
  let rank = 1000;
  const items: Item[] = [];
  for (const parameter of kind.parameters) {
    if (!parameter.name.startsWith(head + '.')) {
      continue;
    }
    const next = parameter.name.slice(head.length + 1).split('.')[0] ?? '';
    if (next.length > 0 && !seen.has(next)) {
      seen.add(next);
      items.push({ label: next, type: 'port', rank: rank-- });
    }
  }
  return items;
}

function describeParameter(parameter: ParameterMeta): string {
  const parts = [parameter.dimension ?? (parameter.valueKind === 'symbol' ? 'symbol' : 'number')];
  if (parameter.unit !== null) {
    parts.push(parameter.unit);
  }
  if (parameter.usualRange !== null) {
    parts.push(`typically ${parameter.usualRange.min}…${parameter.usualRange.max}`);
  }
  return parts.join(' · ');
}

function parameters(
  kind: Kind | null,
  written: Set<string>,
  declared?: {
    readonly parameters: Readonly<Record<string, unknown>>;
    readonly layers?: number;
  } | null,
): Item[] {
  if (kind === null) {
    return [];
  }
  const items: Item[] = [];
  let rank = 1000;
  for (const parameter of kind.parameters) {
    if (
      written.has(normalize(parameter.name)) ||
      parameter.aliases.some((a) => written.has(normalize(a)))
    ) {
      continue;
    }
    items.push({
      label: parameter.name,
      type: 'parameter',
      detail: describeParameter(parameter),
      info: omissionText(parameter),
      rank: rank--,
    });
  }
  const layers = declared?.layers;
  for (const family of kind.indexedParameters) {
    const max =
      family.maxIndex ??
      (family.maxIndexParameter !== null && layers !== undefined ? layers : null);
    if (max === null) {
      // The count depends on a parameter not yet known: offer the template.
      items.push({
        label: family.pattern,
        insert: family.pattern.replace('{index}', '1'),
        type: 'template',
        detail: describeParameter(family.element),
        rank: rank--,
      });
      continue;
    }
    const limit = Math.min(max, 16);
    for (let index = family.minIndex; index <= limit; index++) {
      const name = family.pattern.replace('{index}', String(index));
      if (!written.has(normalize(name))) {
        items.push({
          label: name,
          type: 'parameter',
          detail: describeParameter(family.element),
          rank: rank--,
        });
      }
    }
  }
  return items;
}

function omissionText(parameter: ParameterMeta): string {
  switch (parameter.omission) {
    case 'size':
      return 'Sized when omitted';
    case 'default':
      return `Defaults to ${parameter.default ?? '?'}${parameter.defaultBasis === null ? '' : ` — ${parameter.defaultBasis}`}`;
    default:
      return 'Required';
  }
}

// ---- values ---------------------------------------------------------------------------------------

/**
 * Values filtered to a dimension (`52` invariant 5b): the lets of it, the properties of it, and its
 * units -- or, where the dimension is unknown, every let and property and no filter. A word-valued
 * parameter offers its words and nothing else.
 */
function values(here: Here, want: Want | null, withCurves = false): Item[] {
  const metadata = here.metadata;
  const sources = here.sources;
  const dimension = want?.dimension ?? null;
  const filtered = want !== null && dimension !== null;
  const items: Item[] = [];
  let rank = 1000;

  if (want !== null && want.words.length > 0) {
    return want.words.map((word) => ({ label: word, type: 'value', rank: rank-- }));
  }

  for (const binding of sources.model?.bindings ?? []) {
    const deferred = binding.value === null;
    const unnamed = binding.dimension === null && binding.siUnit !== null;
    // A deferred let with a typed dimension is filtered like any other (U-5); one nothing typed is
    // offered everywhere, dimmed, since the filter has nothing to go on.
    const untyped = deferred && binding.dimension === null && binding.siUnit === null;
    if (filtered && binding.dimension !== dimension && !untyped) {
      continue;
    }
    const value = deferred ? '—' : `${binding.value} ${binding.unit ?? ''}`.trim();
    items.push({
      label: binding.name,
      type: 'let',
      detail: `${unnamed ? binding.siUnit : (binding.dimension ?? (deferred ? 'deferred' : 'dimensionless'))} · ${value}`,
      dimmed: unnamed || deferred,
      rank: rank--,
    });
  }

  if (withCurves || !filtered) {
    items.push(...curves(here, rank));
    rank -= items.length;
  }

  for (const component of sources.model?.components ?? []) {
    const kind = kindByKeyword(metadata, component.kind);
    if (kind === null) {
      continue;
    }
    for (const property of kind.properties) {
      if (filtered && property.dimension !== dimension) {
        continue;
      }
      items.push({
        label: `${component.id}.${property.name}`,
        type: 'reference',
        detail: `${property.dimension ?? 'dimensionless'} · ${property.unit} · ${property.availability}`,
        rank: rank--,
      });
    }
  }

  if (filtered) {
    const entry = metadata.dimensions.find((d) => d.name === dimension);
    for (const unit of entry?.units ?? []) {
      items.push({ label: unit, type: 'unit', detail: dimension, rank: rank-- });
    }
  }

  return items;
}

/** Names a document writes in a list, `cases = [winter, mild]`: the script's own vocabulary. */
function namesInDocument(doc: string, pattern: RegExp): string[] {
  const match = pattern.exec(doc);
  return match === null
    ? []
    : (match[1] ?? '')
        .split(',')
        .map((s) => s.trim())
        .filter((s) => /^\w+$/.test(s));
}

/** The curves the document declares, `curve heat_demand: outdoor`, by name. */
function curves(here: Here, start = 400): Item[] {
  const items: Item[] = [];
  let rank = start;
  for (const match of here.doc.matchAll(/^\s*curve\s+(\w+)\s*:\s*(\w+)?/gm)) {
    items.push({
      label: match[1] ?? '',
      type: 'reference',
      detail: `a curve of ${match[2] ?? '?'}`,
      rank: rank--,
    });
  }
  return items;
}

/** A curve's or a controller's driver: a let, or the run's clock. */
function drivers(here: Here): Item[] {
  let rank = 1000;
  const items: Item[] = (here.sources.model?.bindings ?? []).map((binding) => ({
    label: binding.name,
    type: 'let',
    detail: binding.dimension ?? 'a let',
    rank: rank--,
  }));
  items.push({ label: 'time', type: 'value', detail: "the run's clock", rank: rank-- });
  return items;
}

/** What a controller may move: a component whose kind has an actuated parameter (`D-61`). */
function actuators(here: Here): Item[] {
  let rank = 1000;
  const items: Item[] = [];
  for (const component of here.sources.model?.components ?? []) {
    const kind = kindByKeyword(here.metadata, component.kind);
    if (kind?.actuatedParameter != null) {
      items.push({
        label: component.id,
        type: 'name',
        detail: `${component.kind} · moves its ${kind.actuatedParameter}`,
        rank: rank--,
      });
    }
  }
  return items;
}

/** What a controller may read: a sensor, by what it measures. */
function measurements(here: Here): Item[] {
  let rank = 1000;
  const items: Item[] = [];
  for (const component of here.sources.model?.components ?? []) {
    const kind = kindByKeyword(here.metadata, component.kind);
    if (kind?.measuredProperty != null) {
      items.push({
        label: component.id,
        type: 'name',
        detail: `${component.kind} · reads ${kind.measuredProperty}`,
        rank: rank--,
      });
    }
  }
  return items;
}

// ---- names, ports, properties, targets --------------------------------------------------------------

function componentNames(here: Here, includeInferred: boolean, start = 1000): Item[] {
  const items: Item[] = [];
  let rank = start;
  for (const component of here.sources.model?.components ?? []) {
    const inferred = component.origin !== 'declared';
    if (inferred && !includeInferred) {
      continue;
    }
    items.push({
      label: component.id,
      type: 'name',
      detail: inferred ? `${component.kind} · inferred` : component.kind,
      rank: rank--,
    });
  }
  return items;
}

function kindOfComponent(name: string, here: Here): Kind | null {
  const component = here.sources.model?.components.find((c) => c.id === name);
  return component === undefined ? null : kindByKeyword(here.metadata, component.kind);
}

function componentNamed(
  name: string,
  sources: Sources,
): { parameters: Readonly<Record<string, unknown>>; layers?: number } | null {
  const component = sources.model?.components.find((c) => c.id === name);
  if (component === undefined) {
    return null;
  }
  const layers = (component.parameters as Record<string, { value?: number | null } | undefined>)[
    'layers'
  ]?.value;
  return { parameters: component.parameters, ...(typeof layers === 'number' ? { layers } : {}) };
}

/**
 * A component's ports as a script writes them after its name and a dot (`D-179`): `in`, `out`,
 * `secondary.in`, `primary.in`; after `HX1.secondary.`, the rest. A tank's families add the members
 * the model materialized and the next one as a template.
 */
function ports(kind: Kind | null, owner: string, sources: Sources, path: string): Item[] {
  if (kind === null) {
    return [];
  }
  const items: Item[] = [];
  let rank = 1000;
  const lead = path.length === 0 ? '' : path + '.';
  const offer = (spelling: string, detail: string): void => {
    if (spelling.startsWith(lead) && spelling.length > lead.length) {
      items.push({ label: spelling.slice(lead.length), type: 'port', detail, rank: rank-- });
    }
  };
  for (const port of kind.ports) {
    offer(port.spelling, port.role);
    for (const alias of port.aliases) {
      offer(alias, `${port.role} · also ${port.spelling}`);
    }
  }
  if (lead.length > 0) {
    return items;
  }
  const component = sources.model?.components.find((c) => c.id === owner);
  const materialized = new Set(component?.ports.map((p) => p.name) ?? []);
  for (const family of kind.portFamilies) {
    // The model's port ids are the keys (`in2`); the script writes the pattern (`in[2]`), and the
    // first member is the fixed port already listed above (D-120).
    const spell = (index: number): string => family.pattern.replace('{index}', String(index));
    let count = 0;
    for (const name of materialized) {
      const digits = name.startsWith(family.prefix) ? name.slice(family.prefix.length) : '';
      if (!/^\d+$/.test(digits)) {
        continue;
      }
      count++;
      const index = Number(digits);
      if (index >= family.minIndex) {
        items.push({
          label: spell(index),
          type: 'port',
          detail: `${family.role} · materialized`,
          rank: rank--,
        });
      }
    }
    const next = Math.max(count + 1, family.minIndex);
    if (next <= family.maxIndex) {
      items.push({
        label: `${family.prefix}[${family.minIndex}..${family.maxIndex}]`,
        insert: spell(next),
        type: 'template',
        detail: `${family.role} · the next is ${spell(next)}`,
        rank: rank--,
      });
    }
  }
  return items;
}

function properties(kind: Kind | null, path: string): Item[] {
  if (kind === null) {
    return [];
  }
  let rank = 1000;
  const lead = path.length === 0 ? '' : path + '.';
  const items: Item[] = kind.properties
    .filter((property: PropertyMeta) => property.name.startsWith(lead))
    .map((property: PropertyMeta) => ({
      label: property.name.slice(lead.length),
      type: 'property',
      detail: `${property.dimension ?? 'dimensionless'} · ${property.unit} · ${property.availability}`,
      rank: rank--,
    }));
  if (lead.length === 0) {
    for (const family of kind.indexedProperties) {
      items.push({
        label: family.pattern,
        insert: family.pattern.replace('{index}', '1'),
        type: 'template',
        detail: family.element.dimension ?? '',
        rank: rank--,
      });
    }
  }
  return items;
}

/** A run's target on a component: its quantity parameters, and a controller's setpoint (`19` §Runs). */
function targets(kind: Kind | null, path: string): Item[] {
  if (kind === null) {
    return [];
  }
  let rank = 1000;
  const lead = path.length === 0 ? '' : path + '.';
  const items: Item[] = kind.parameters
    .filter((p) => p.valueKind === 'quantity' && p.name.startsWith(lead))
    .map((p) => ({ label: p.name.slice(lead.length), type: 'target', detail: describeParameter(p), rank: rank-- }));
  if (kind.keyword === 'controller' && lead.length === 0) {
    items.unshift({ label: 'setpoint', type: 'target', detail: 'in what the controller measures', rank: 1001 });
  }
  return items;
}

/** The parameter a run's target names, `RAD.power`, so its value can be filtered by its dimension. */
function targetParameter(here: Here, name: string): ParameterMeta | null {
  const dot = name.indexOf('.');
  if (dot < 0) {
    return null;
  }
  return parameterOf(kindOfComponent(name.slice(0, dot), here), name.slice(dot + 1));
}

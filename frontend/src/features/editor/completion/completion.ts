import type {
  Kind,
  Metadata,
  ModelContract,
  ParameterMeta,
  PropertyMeta,
} from '../../../api/types.ts';
import { ambiguityMargin, resolveThreshold } from '../language/lexicon.generated.ts';
import {
  initialState,
  tokenizeLine,
  type LineToken,
  type Section,
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
    | 'kind'
    | 'parameter'
    | 'let'
    | 'reference'
    | 'unit'
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
  | 'kind'
  | 'parameter'
  | 'value'
  | 'connection-name'
  | 'port'
  | 'property'
  | 'schedule-target'
  | 'let-value';

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

/**
 * Completion at a position (`52`'s table): contextual on the line's tokens before the cursor and
 * the section the line is in. Everything offered comes from `/metadata` or the model (invariant 5);
 * with no metadata the list is empty and typing is unaffected (`52` error cases).
 */
export function complete(position: Position, sources: Sources): Completion {
  const { lineStart, line, column, section } = locate(position);
  const before = line.slice(0, column);
  const state: TokenizerState = { section };
  const tokens = tokenizeLine(before, state).filter((t) => t.kind !== 'Comment');
  const wordStart = wordStartAt(before);
  const prefix = before.slice(wordStart);
  const from = lineStart + wordStart;
  const preceding = tokens.filter((t) => t.to <= wordStart);
  const last = preceding[preceding.length - 1];
  const beforeLast = preceding[preceding.length - 2];
  const metadata = sources.metadata;

  const none = (context: ContextKind): Completion => ({ from, items: [], context });
  if (metadata === null) {
    return none('none');
  }

  const first = preceding[0];
  const directive = first?.kind === 'Keyword' && !(first.role === 'kind');

  // After a dot on a declaration line, behind a port: the port's state -- `in.` offers `t`,
  // `in[2].` offers `t`, `flow`, `dp`, `dt` (D-120). Checked before the reference rule below,
  // since `in` is not a component.
  if (
    last?.kind === 'Punctuation' &&
    last.text === '.' &&
    !directive &&
    section !== 'connections' &&
    section !== 'schedule' &&
    preceding.length >= 3 &&
    preceding[1] !== undefined
  ) {
    const kind = resolveKind(metadata, preceding[1].text);
    const head = nameEndingAt(preceding, preceding.length - 2);
    const items = kind === null || head === null ? [] : portQuantities(kind, preceding, head);
    if (items.length > 0) {
      return { from, items, context: 'parameter' };
    }
  }

  // After a dot: a port in a connection, else a property.
  if (last?.kind === 'Punctuation' && last.text === '.' && beforeLast?.kind === 'Identifier') {
    const owner = beforeLast.text;
    const kind = kindOfComponent(owner, sources);
    if (section === 'connections' || (section === 'schedule' && preceding.length === 2)) {
      if (section === 'schedule') {
        return { from, items: targets(kind, prefix), context: 'schedule-target' };
      }
      return { from, items: ports(kind, owner, sources, prefix), context: 'port' };
    }
    return { from, items: properties(kind, prefix), context: 'property' };
  }

  if (section === 'schedule' && !directive) {
    if (preceding.length === 0) {
      return { from, items: componentNames(sources, prefix, false), context: 'schedule-target' };
    }
    return none('none');
  }

  if (section === 'connections' && !directive) {
    if (preceding.length === 0 || (last?.kind === 'Punctuation' && last.text === '-')) {
      return { from, items: componentNames(sources, prefix, true), context: 'connection-name' };
    }
    if (last?.kind === 'Punctuation' && last.text === '=' && beforeLast?.kind === 'Identifier') {
      // A pipe property on the connection line (I7): the pipe kind's parameter.
      return {
        from,
        items: values(
          metadata,
          sources,
          parameterOf(metadata, 'pipe', nameEndingAt(preceding, preceding.length - 2) ?? ''),
          prefix,
        ),
        context: 'value',
      };
    }
    if (last?.kind === 'Identifier' && preceding.length >= 3) {
      return {
        from,
        items: parameters(kindByKeyword(metadata, 'pipe'), preceding, prefix),
        context: 'parameter',
      };
    }
    return none('none');
  }

  // A let's value: everything that has a value, unfiltered, since the let has no dimension yet.
  if (directive && first?.text === 'let') {
    if (last?.kind === 'Punctuation' && last.text === '=') {
      return { from, items: values(metadata, sources, null, prefix), context: 'let-value' };
    }
    if (last?.kind === 'Punctuation' && '+-*/('.includes(last.text)) {
      return { from, items: values(metadata, sources, null, prefix), context: 'let-value' };
    }
    return none('none');
  }

  if (directive) {
    return none('none');
  }

  // A declaration line. Position 0: the name; nothing is offered (the user is naming a component).
  if (preceding.length === 0) {
    return none('none');
  }

  // Position 1: the kind, alias-aware.
  if (preceding.length === 1 && preceding[0]?.kind === 'Identifier') {
    return { from, items: kinds(metadata, prefix), context: 'kind' };
  }

  const kindToken = preceding[1];
  const kind = kindToken === undefined ? null : resolveKind(metadata, kindToken.text);

  // After `param=`: the value, filtered by the parameter's dimension. The name may be a port's
  // state, `in[2].t=`, which runs back from the `=` over the dots and brackets (D-120).
  if (last?.kind === 'Punctuation' && last.text === '=' && beforeLast?.kind === 'Identifier') {
    const name = nameEndingAt(preceding, preceding.length - 2) ?? beforeLast.text;
    const parameter = kind === null ? null : parameterOf(metadata, kind.keyword, name);
    return { from, items: values(metadata, sources, parameter, prefix), context: 'value' };
  }

  // Inside a value expression: references and lets, filtered as the parameter is.
  if (last?.kind === 'Punctuation' && '+-*/('.includes(last.text)) {
    const parameterName = parameterBefore(preceding);
    const parameter =
      kind === null || parameterName === null
        ? null
        : parameterOf(metadata, kind.keyword, parameterName);
    return { from, items: values(metadata, sources, parameter, prefix), context: 'value' };
  }

  // After the kind or a completed parameter: the kind's remaining parameters.
  if (
    kind !== null &&
    (last?.kind === 'Identifier' ||
      last?.kind === 'Keyword' ||
      last?.kind === 'NumberLiteral' ||
      last?.kind === 'QuantityLiteral' ||
      last?.kind === 'StringLiteral' ||
      (last?.kind === 'Punctuation' && last.text === ']'))
  ) {
    return {
      from,
      items: parameters(kind, preceding, prefix, componentNamed(preceding[0]?.text ?? '', sources)),
      context: 'parameter',
    };
  }

  return none('none');
}

// ---- context ---------------------------------------------------------------------------------------

function locate(position: Position): {
  lineStart: number;
  line: string;
  column: number;
  section: Section;
} {
  const doc = position.doc;
  const lineStart = doc.lastIndexOf('\n', position.offset - 1) + 1;
  const lineEnd = doc.indexOf('\n', position.offset);
  const line = doc.slice(lineStart, lineEnd < 0 ? doc.length : lineEnd);
  const state = initialState();
  let at = 0;
  while (at < lineStart) {
    const end = doc.indexOf('\n', at);
    const stop = end < 0 ? doc.length : end;
    tokenizeLine(doc.slice(at, stop), state);
    at = stop + 1;
  }
  return { lineStart, line, column: position.offset - lineStart, section: state.section };
}

function wordStartAt(before: string): number {
  let i = before.length;
  while (i > 0 && /[A-Za-z0-9_]/.test(before.charAt(i - 1))) {
    i--;
  }
  return i;
}

function parameterBefore(tokens: readonly LineToken[]): string | null {
  for (let i = tokens.length - 1; i >= 1; i--) {
    const token = tokens[i];
    if (
      token?.kind === 'Punctuation' &&
      token.text === '=' &&
      tokens[i - 1]?.kind === 'Identifier'
    ) {
      return nameEndingAt(tokens, i - 1);
    }
  }
  return null;
}

/**
 * The parameter name whose last token is `tokens[end]`: `power`, or `in[2].t` -- whatever runs
 * back without a gap over words, dots, brackets and the index between them, the adjacency the
 * parser demands (`12`, D-120). Null when `tokens[end]` is neither a word nor a closing bracket.
 */
function nameEndingAt(tokens: readonly LineToken[], end: number): string | null {
  const tail = tokens[end];
  if (tail?.kind !== 'Identifier' && !(tail?.kind === 'Punctuation' && tail.text === ']')) {
    return null;
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
      (part.kind === 'Punctuation' &&
        (part.text === '.' || part.text === '[' || part.text === ']'));
    if (!joins) {
      break;
    }
    start = j;
    from = part.from;
  }
  return tokens
    .slice(start, end + 1)
    .map((t) => t.text)
    .join('');
}

/** The names already written on the line, `power` and `in[2].t` alike, normalised. */
function writtenNames(preceding: readonly LineToken[]): Set<string> {
  const written = new Set<string>();
  for (let i = 1; i + 1 < preceding.length; i++) {
    const next = preceding[i + 1];
    if (next?.kind === 'Punctuation' && next.text === '=') {
      const name = nameEndingAt(preceding, i);
      if (name !== null) {
        written.add(normalize(name));
      }
    }
  }
  return written;
}

/** Escapes a family pattern's brackets and dots and turns `{index}` into a capture. */
function familyPattern(pattern: string): RegExp {
  const escaped = pattern.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp('^' + escaped.replace('\\{index\\}', '(\\d+)') + '$');
}

// ---- kinds ------------------------------------------------------------------------------------------

function kindByKeyword(metadata: Metadata, keyword: string): Kind | null {
  return metadata.kinds.find((k) => k.keyword === keyword) ?? null;
}

/** Resolves a written kind as the binder does: normalised, then aliases, then similarity above the threshold. */
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
  const ranked = rankKinds(metadata, written);
  const best = ranked[0];
  const next = ranked[1];
  if (best === undefined || best.score < resolveThreshold) {
    return null;
  }
  if (next !== undefined && best.score - next.score < ambiguityMargin) {
    return null;
  }
  return best.kind;
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
    detail: r.via === null ? describeKind(r.kind) : `via '${r.via}'`,
    ...(ambiguous && index < 2 ? { ambiguous: true } : {}),
    rank: 1000 - index,
  }));
}

function describeKind(kind: Kind): string {
  const ports = kind.ports.map((p) => p.name).join(', ');
  return ports.length === 0 ? '' : `Ports: ${ports}`;
}

// ---- parameters -----------------------------------------------------------------------------------

function parameterOf(metadata: Metadata, keyword: string, written: string): ParameterMeta | null {
  const kind = kindByKeyword(metadata, keyword);
  if (kind === null) {
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

/**
 * The quantities a port's state may be written with, for `in.` or `in[2].`: every parameter of the
 * kind whose name is `{head}.{quantity}`, minus the ones the line already states (D-120).
 */
function portQuantities(kind: Kind, preceding: readonly LineToken[], head: string): Item[] {
  const written = writtenNames(preceding);
  const folded = head.replace('[1]', '');
  const items: Item[] = [];
  let rank = 1000;
  for (const parameter of kind.parameters) {
    const dot = parameter.name.indexOf('.');
    if (dot < 0) {
      continue;
    }
    const port = parameter.name.slice(0, dot);
    if ((port !== folded && port !== head) || written.has(normalize(parameter.name))) {
      continue;
    }
    items.push({
      label: parameter.name.slice(dot + 1),
      type: 'parameter',
      detail: describeParameter(parameter),
      info: omissionText(parameter),
      rank: rank--,
    });
  }
  for (const family of kind.indexedParameters) {
    const match = familyPattern(family.pattern.slice(0, family.pattern.indexOf('.'))).exec(head);
    if (match === null || family.pattern.indexOf('.') < 0) {
      continue;
    }
    const name = family.pattern.replace('{index}', match[1] ?? '');
    if (!written.has(normalize(name))) {
      items.push({
        label: family.pattern.slice(family.pattern.indexOf('.') + 1),
        type: 'parameter',
        detail: describeParameter(family.element),
        rank: rank--,
      });
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
  preceding: readonly LineToken[],
  prefix: string,
  declared?: {
    readonly parameters: Readonly<Record<string, unknown>>;
    readonly layers?: number;
  } | null,
): Item[] {
  if (kind === null) {
    return [];
  }
  const written = writtenNames(preceding);
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
  void prefix;
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

function values(
  metadata: Metadata,
  sources: Sources,
  parameter: ParameterMeta | null,
  prefix: string,
): Item[] {
  // 52 invariant 5b: filtered to the parameter's dimension, or the dimension is unknown and the filter is off.
  const dimension = parameter?.dimension ?? null;
  const filtered = parameter !== null && dimension !== null;
  const items: Item[] = [];
  let rank = 1000;

  if (parameter !== null && parameter.valueKind === 'symbol') {
    return parameter.acceptedSymbols.map((symbol) => ({
      label: symbol,
      type: 'unit',
      rank: rank--,
    }));
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

  void prefix;
  return items;
}

// ---- names, ports, properties, targets --------------------------------------------------------------

function componentNames(sources: Sources, prefix: string, includeInferred: boolean): Item[] {
  const items: Item[] = [];
  let rank = 1000;
  for (const component of sources.model?.components ?? []) {
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
  void prefix;
  return items;
}

function kindOfComponent(name: string, sources: Sources): Kind | null {
  const metadata = sources.metadata;
  const component = sources.model?.components.find((c) => c.id === name);
  return metadata === null || component === undefined
    ? null
    : kindByKeyword(metadata, component.kind);
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

function ports(kind: Kind | null, owner: string, sources: Sources, prefix: string): Item[] {
  if (kind === null) {
    return [];
  }
  const items: Item[] = [];
  let rank = 1000;
  const component = sources.model?.components.find((c) => c.id === owner);
  const materialized = new Set(component?.ports.map((p) => p.name) ?? []);
  for (const port of kind.ports) {
    items.push({ label: port.name, type: 'port', detail: port.role, rank: rank-- });
  }
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
  void prefix;
  return items;
}

function properties(kind: Kind | null, prefix: string): Item[] {
  if (kind === null) {
    return [];
  }
  let rank = 1000;
  const items: Item[] = kind.properties.map((property: PropertyMeta) => ({
    label: property.name,
    type: 'property',
    detail: `${property.dimension ?? 'dimensionless'} · ${property.unit} · ${property.availability}`,
    rank: rank--,
  }));
  for (const family of kind.indexedProperties) {
    items.push({
      label: family.pattern,
      insert: family.pattern.replace('{index}', '1'),
      type: 'template',
      detail: family.element.dimension ?? '',
      rank: rank--,
    });
  }
  void prefix;
  return items;
}

function targets(kind: Kind | null, prefix: string): Item[] {
  if (kind === null) {
    return [];
  }
  let rank = 1000;
  void prefix;
  return kind.parameters
    .filter((p) => p.valueKind === 'quantity')
    .map((p) => ({ label: p.name, type: 'target', detail: describeParameter(p), rank: rank-- }));
}

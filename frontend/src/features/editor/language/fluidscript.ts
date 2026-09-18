import {
  HighlightStyle,
  StreamLanguage,
  syntaxHighlighting,
  type StringStream,
} from '@codemirror/language';
import { Tag, tags } from '@lezer/highlight';

import {
  initialState,
  tokenizeLine,
  type LineToken,
  type TokenizerState,
  type TokenRole,
} from './tokenizer.ts';

/** Two roles VS Code's palette gives their own colour and Lezer has no tag for. */
const kindTag = Tag.define(tags.typeName);
const declarationTag = Tag.define(tags.variableName);

const tagOf: Readonly<Record<TokenRole, Tag>> = {
  keyword: tags.keyword,
  kind: kindTag,
  declaration: declarationTag,
  name: tags.variableName,
  parameter: tags.attributeName,
  number: tags.number,
  unit: tags.unit,
  string: tags.string,
  comment: tags.lineComment,
  operator: tags.operator,
  reference: tags.propertyName,
  port: tags.propertyName,
  function: tags.function(tags.variableName),
  word: tags.variableName,
  unknown: tags.invalid,
};

interface StreamState {
  tokenizer: TokenizerState;
  line: LineToken[] | null;
}

/**
 * The FluidScript language for CodeMirror: `52`'s highlighter as a stream language over
 * `tokenizeLine`. The language is line-granular, so a stream tokenizer with the section as its
 * only state is the incremental parse; nothing here waits on the network (`52` invariant 2).
 */
export const fluidscriptLanguage = StreamLanguage.define<StreamState>({
  name: 'fluidscript',
  startState: () => ({ tokenizer: initialState(), line: null }),
  copyState: (state) => ({ tokenizer: { ...state.tokenizer }, line: state.line }),
  token(stream: StringStream, state: StreamState): string | null {
    if (stream.sol()) {
      state.line = tokenizeLine(stream.string, state.tokenizer);
    }
    const tokens = state.line ?? [];
    const token = tokens.find((t) => t.from >= stream.pos) ?? null;
    if (token === null) {
      stream.skipToEnd();
      return null;
    }
    if (token.from > stream.pos) {
      stream.pos = token.from;
      return null;
    }
    stream.pos = token.to;
    return tagName(token.role);
  },
  blankLine: () => undefined,
  languageData: { commentTokens: { line: '#' } },
  tokenTable: Object.fromEntries(
    (Object.keys(tagOf) as TokenRole[]).map((role) => [tagName(role), tagOf[role]]),
  ),
});

function tagName(role: TokenRole): string {
  return `fs-${role}`;
}

/**
 * The VS Code mapping as CSS classes on `55`'s syntax tokens, so the theme owns every colour and the
 * editor names none: `.syn-keyword` is `--syn-keyword`, and the two dimmed roles carry their opacity.
 */
export const highlightStyle = HighlightStyle.define([
  { tag: tags.keyword, class: 'syn-keyword' },
  { tag: kindTag, class: 'syn-kind' },
  { tag: declarationTag, class: 'syn-identifier' },
  { tag: tags.variableName, class: 'syn-identifier' },
  { tag: tags.attributeName, class: 'syn-parameter' },
  { tag: tags.number, class: 'syn-number' },
  { tag: tags.unit, class: 'syn-unit' },
  { tag: tags.string, class: 'syn-string' },
  { tag: tags.lineComment, class: 'syn-comment' },
  { tag: tags.operator, class: 'syn-operator' },
  { tag: tags.propertyName, class: 'syn-reference' },
  { tag: tags.function(tags.variableName), class: 'syn-function' },
  { tag: tags.invalid, class: 'syn-invalid' },
]);

/** The highlighting extension over `highlightStyle`. */
export const fluidscriptHighlighting = syntaxHighlighting(highlightStyle);

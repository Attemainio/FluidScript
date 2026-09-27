import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

/** The lexicon the Api commits (`LexiconWire`, `52` invariants 2 and 5). */
export interface Lexicon {
  readonly statementWords: readonly string[];
  readonly eventWords: readonly string[];
  readonly unitSymbols: readonly string[];
  readonly resolveThreshold: number;
  readonly ambiguityMargin: number;
  readonly suggestionFloor: number;
}

const source = new URL(
  '../../src/FluidScript.Api/Contracts/Schemas/language.json',
  import.meta.url,
);

/** Renders `lexicon.generated.ts` from the committed `language.json`. Node-only: it reads the repository. */
export function renderLexicon(): string {
  const lexicon = JSON.parse(readFileSync(fileURLToPath(source), 'utf8')) as Lexicon;
  const list = (items: readonly string[]): string =>
    items.map((s) => `  '${s.replaceAll("'", "\\'")}',`).join('\n');
  return [
    '/* Generated from src/FluidScript.Api/Contracts/Schemas/language.json by npm run types. Do not edit. */',
    '',
    "/** The words that open a statement at a line's start, in `19`'s order; the lexer reserves none. */",
    'export const statementWords: readonly string[] = [',
    list(lexicon.statementWords),
    '];',
    '',
    '/** The words that open an event, inside a run only. */',
    'export const eventWords: readonly string[] = [',
    list(lexicon.eventWords),
    '];',
    '',
    '/** Every unit spelling a script may write after a number, longest first, as the lexer probes them (maximal munch). */',
    'export const unitSymbols: readonly string[] = [',
    list(lexicon.unitSymbols),
    '];',
    '',
    '/** The similarity at which a misspelled kind or parameter is offered as the fix (`D-15`; since `D-170` it never binds). */',
    `export const resolveThreshold = ${lexicon.resolveThreshold};`,
    '',
    '/** How far clear of the runner-up a match must be, or both are reported. */',
    `export const ambiguityMargin = ${lexicon.ambiguityMargin};`,
    '',
    '/** The score below which a failed match carries no suggestion. */',
    `export const suggestionFloor = ${lexicon.suggestionFloor};`,
    '',
  ].join('\n');
}

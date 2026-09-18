import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

/** The lexicon the Api commits (`LexiconWire`, `52` invariants 2 and 5). */
export interface Lexicon {
  readonly reservedWords: readonly string[];
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
    "/** Every reserved word, in `12`'s order. */",
    'export const reservedWords: readonly string[] = [',
    list(lexicon.reservedWords),
    '];',
    '',
    '/** Every accepted unit spelling, longest first, as the lexer probes them (maximal munch). */',
    'export const unitSymbols: readonly string[] = [',
    list(lexicon.unitSymbols),
    '];',
    '',
    '/** The similarity a written kind or parameter must reach to resolve (`D-15`). */',
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

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { renderLexicon } from '../../../../scripts/lexicon.ts';

describe('the lexicon', () => {
  it('is what the committed language.json renders to', () => {
    // 52 invariant 5: the reserved words, the unit symbols and the thresholds come from the server,
    // through the file the Api commits and gates; a drift regenerates in place and fails.
    const path = fileURLToPath(new URL('./lexicon.generated.ts', import.meta.url));
    const expected = renderLexicon();
    const actual = readFileSync(path, 'utf8');

    if (actual !== expected) {
      writeFileSync(path, expected);
    }

    expect(
      actual,
      'lexicon.generated.ts did not match language.json and has been regenerated; review and rerun',
    ).toBe(expected);
  });
});

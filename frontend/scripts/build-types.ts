// Writes src/api/types.generated.ts from the JSON schemas the Api commits (D-46 step 3), and
// src/features/editor/language/lexicon.generated.ts from its language.json. The tests in
// src/api/types.test.ts and src/features/editor/language/lexicon.test.ts render the same and fail
// when a checked-in file differs.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { renderLexicon } from './lexicon.ts';
import { renderWireTypes } from './wireTypes.ts';

const types = fileURLToPath(new URL('../src/api/types.generated.ts', import.meta.url));
writeFileSync(types, await renderWireTypes());
console.log(`wrote ${types}`);

const lexicon = fileURLToPath(
  new URL('../src/features/editor/language/lexicon.generated.ts', import.meta.url),
);
writeFileSync(lexicon, renderLexicon());
console.log(`wrote ${lexicon}`);

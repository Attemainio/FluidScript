// Writes src/api/types.generated.ts from the JSON schemas the Api commits (D-46 step 3). The test
// in src/api/types.test.ts renders the same and fails when the checked-in file differs.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { renderWireTypes } from './wireTypes.ts';

const target = fileURLToPath(new URL('../src/api/types.generated.ts', import.meta.url));
writeFileSync(target, await renderWireTypes());
console.log(`wrote ${target}`);

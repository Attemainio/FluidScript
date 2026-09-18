// Writes src/design/themes.generated.css from the two theme files. The test in
// src/design/themeCss.test.ts does the same and fails when the checked-in file differs, so this
// script is a convenience, not the gate.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { builtinThemes } from '../src/design/builtin.ts';
import { renderThemeCss } from '../src/design/themeCss.ts';

const target = fileURLToPath(new URL('../src/design/themes.generated.css', import.meta.url));
writeFileSync(target, renderThemeCss(builtinThemes.light, builtinThemes.dark));
console.log(`wrote ${target}`);

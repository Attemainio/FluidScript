import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import type { ModelContract } from '../api/types.ts';

/** The Api's committed solved goldens (`42`, `SchemaTests`), the scenes the canvas is tested on. */
export const goldensDir = fileURLToPath(
  new URL('../../../tests/FluidScript.Api.Tests/Contracts/Goldens/', import.meta.url),
);

export function solvedGoldens(): { name: string; model: ModelContract }[] {
  return readdirSync(goldensDir)
    .filter((file) => file.endsWith('.solved.json'))
    .sort()
    .map((file) => ({
      name: file.replace('.solved.json', ''),
      model: JSON.parse(readFileSync(goldensDir + file, 'utf8')) as ModelContract,
    }));
}

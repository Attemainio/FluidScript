import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import type { ModelContract } from '../api/types.ts';

/** The Api's committed solved goldens (`42`, `SchemaTests`), the scenes the canvas is tested on. */
// From the working directory rather than `import.meta.url`, which is not a file URL under jsdom.
export const goldensDir = `${resolve(process.cwd(), '../tests/FluidScript.Api.Tests/Contracts/Goldens')}/`;

/** The repository's `samples/`, for a test that needs the text a golden was compiled from. */
export const samplesDir = `${resolve(process.cwd(), '../samples')}/`;

export function solvedGoldens(): { name: string; model: ModelContract }[] {
  return readdirSync(goldensDir)
    .filter((file) => file.endsWith('.solved.json'))
    .sort()
    .map((file) => ({
      name: file.replace('.solved.json', ''),
      model: JSON.parse(readFileSync(goldensDir + file, 'utf8')) as ModelContract,
    }));
}

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { renderWireTypes } from '../../scripts/wireTypes.ts';

describe('the wire types', () => {
  it('are what the committed schemas generate', async () => {
    // D-46 step 3: the TypeScript DTOs are generated from the committed schema and drift fails here.
    // The docs gate's rule: regenerate in place on a mismatch and fail, so the diff is reviewed.
    const path = fileURLToPath(new URL('./types.generated.ts', import.meta.url));
    const expected = await renderWireTypes();
    const actual = readFileSync(path, 'utf8');

    if (actual !== expected) {
      writeFileSync(path, expected);
    }

    expect(
      actual,
      'types.generated.ts did not match the schemas and has been regenerated in place; review the change and run the tests again',
    ).toBe(expected);
  }, 30_000);

  it('name every record once', async () => {
    const rendered = await renderWireTypes();
    const names = [...rendered.matchAll(/^export (?:interface|type) (\w+)/gm)].map((m) => m[1]);

    expect(new Set(names).size).toBe(names.length);
    expect(names.some((n) => /\d$/.test(n ?? ''))).toBe(false);
    for (const name of [
      'ModelContract',
      'CompileResponse',
      'Metadata',
      'Diagnostic',
      'Placement',
      'Route',
      'Quantity',
    ]) {
      expect(names).toContain(name);
    }
  }, 30_000);
});

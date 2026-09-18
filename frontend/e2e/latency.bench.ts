import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { expect, test, type Page } from '@playwright/test';

/**
 * D-48's measurement: a real keypress to the diagnostic decoration for that edit being in the DOM,
 * on the syntax tour and on a 200-declaration script. It is the normative interactive gate of 07 and
 * the one number D-49 sets the debounce from. Three component figures ride along -- the debounce
 * in force, the host's compile time, the response's size -- because a regression in the sum is
 * unactionable without them. Runs on request: `npm run bench`.
 */

interface Hook {
  setText(text: string): void;
  text(): string;
  timings(): { totalMs: number } | null;
  debounceMs: number;
}

declare global {
  interface Window {
    fluidscript?: Hook;
  }
}

const root = fileURLToPath(new URL('../../', import.meta.url));
const tour = readFileSync(`${root}samples/m1-syntax-tour.fluid`, 'utf8');

function twoHundredDeclarations(): string {
  const lines = ['fluidscript 1', 'circuit big', 'fluid water', ''];
  for (let i = 1; i <= 200; i++) {
    lines.push(`HE${i} heat_exchanger power=${10 + (i % 7)} in=20 out=50`);
  }
  lines.push('', 'connections');
  lines.push('N0 - HE1');
  for (let i = 1; i < 200; i++) {
    lines.push(`HE${i} - HE${i + 1}`);
  }
  lines.push('HE200 - N0', 'N0 inlet t=6 p=300', '');
  return lines.join('\n');
}

async function measure(page: Page, script: string, runs: number): Promise<number[]> {
  await page.evaluate((text) => window.fluidscript!.setText(text), script);
  // Let the first compile of this script land and its squiggles settle.
  await page.waitForFunction(() => window.fluidscript!.timings() !== null, null, {
    timeout: 30_000,
  });
  await page.waitForTimeout(500);

  const samples: number[] = [];
  for (let run = 0; run < runs; run++) {
    // The edit: append a parameter no kind has, on the first declaration line, so FS1503 squiggles it.
    const marker = `zz${run}=1`;
    const started = await page.evaluate(
      ({ marker: m }) => {
        const doc = window.fluidscript!.text();
        const at = doc.indexOf('heat_exchanger');
        const line = doc.indexOf('\n', at);
        const target = doc.slice(0, line) + ' ' + m + doc.slice(line);
        const t0 = performance.now();
        window.fluidscript!.setText(target);
        return t0;
      },
      { marker },
    );
    const seen = await page.waitForFunction(
      () => {
        const squiggle = document.querySelector('.cm-lintRange-error');
        return squiggle !== null && squiggle.textContent?.startsWith('zz')
          ? performance.now()
          : null;
      },
      null,
      { timeout: 30_000, polling: 'raf' },
    );
    const seenAt = (await seen.jsonValue()) as number;
    samples.push(seenAt - started);
    await page.evaluate((text) => window.fluidscript!.setText(text), script);
    await page.waitForFunction(() => document.querySelector('.cm-lintRange-error') === null, null, {
      timeout: 30_000,
    });
    await page.waitForTimeout(400);
  }
  return samples;
}

function stats(samples: number[]): { median: number; p95: number; max: number } {
  const sorted = [...samples].sort((a, b) => a - b);
  const at = (q: number): number =>
    sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * q) - 1)] ?? 0;
  return { median: at(0.5), p95: at(0.95), max: sorted[sorted.length - 1] ?? 0 };
}

test('keystroke to visible diagnostic, D-48', async ({ page }) => {
  await page.goto('/');
  await page.waitForFunction(() => window.fluidscript !== undefined);
  const debounce = await page.evaluate(() => window.fluidscript!.debounceMs);

  const results: string[] = [];
  for (const [name, script] of [
    ['m1-syntax-tour', tour],
    ['200 declarations', twoHundredDeclarations()],
  ] as const) {
    const samples = await measure(page, script, 10);
    const compile = await page.evaluate(() => window.fluidscript!.timings()?.totalMs ?? -1);
    const s = stats(samples);
    results.push(
      `| ${name} | ${s.median.toFixed(0)} | ${s.p95.toFixed(0)} | ${s.max.toFixed(0)} | ${debounce} | ${compile} |`,
    );
    expect(samples.length).toBe(10);
  }

  const report = [
    '# Keystroke to visible diagnostic (D-48)',
    '',
    `Measured ${new Date().toISOString().slice(0, 10)} on this machine: headless Chromium through Playwright, the Vite dev server, the Api host in Debug. Ten edits per script, each appending an unknown parameter to the first declaration and waiting for its error squiggle in the DOM.`,
    '',
    '| Script | median ms | p95 ms | max ms | debounce ms | last compile ms |',
    '|---|---|---|---|---|---|',
    ...results,
    '',
    "07's gates: 250 ms for the syntax tour, 400 ms for 200 declarations. The debounce is the first term; D-49 bounds it below by typing cadence (200 ms) and above by what the gate leaves after the compile.",
    '',
  ].join('\n');
  mkdirSync(`${root}diagnostics`, { recursive: true });
  writeFileSync(`${root}diagnostics/keystroke-latency.md`, report);
  console.log(report);
});

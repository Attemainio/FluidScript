/**
 * Core's `NameResolution`, ported so completion and the binder agree on what resolves (`52`
 * invariant 5): the same normalisation, the same Damerau-Levenshtein score, the thresholds from the
 * lexicon. A test holds the port to Core's own figures.
 */

/** Lowercase, with every underscore and space removed; how the registry is indexed (`D-15`). */
export function normalize(written: string): string {
  let out = '';
  for (const c of written) {
    if (c !== '_' && c !== ' ') {
      out += c.toLowerCase();
    }
  }
  return out;
}

/** How alike two normalised names are, 0..1: one minus the edit distance over the longer length. */
export function score(a: string, b: string): number {
  const longest = Math.max(a.length, b.length);
  if (longest === 0) {
    return 1;
  }
  return 1 - distance(a, b) / longest;
}

/** Damerau-Levenshtein with adjacent transposition (optimal string alignment), as Core computes it. */
export function distance(a: string, b: string): number {
  if (a.length === 0 || b.length === 0) {
    return Math.max(a.length, b.length);
  }
  let previousPrevious = new Array<number>(b.length + 1).fill(0);
  let previous = new Array<number>(b.length + 1).fill(0);
  let current = new Array<number>(b.length + 1).fill(0);
  for (let j = 0; j <= b.length; j++) {
    previous[j] = j;
  }
  for (let i = 1; i <= a.length; i++) {
    current[0] = i;
    for (let j = 1; j <= b.length; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      let best = Math.min(
        (previous[j] ?? 0) + 1,
        (current[j - 1] ?? 0) + 1,
        (previous[j - 1] ?? 0) + cost,
      );
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) {
        best = Math.min(best, (previousPrevious[j - 2] ?? 0) + 1);
      }
      current[j] = best;
    }
    [previousPrevious, previous, current] = [previous, current, previousPrevious];
  }
  return previous[b.length] ?? 0;
}

/**
 * The idle debounce before a compile, milliseconds (`R-21`, `D-49`).
 *
 * A measured value, not a chosen one: its floor is typing cadence, about 200 ms between keys at
 * 40 wpm, so a pause mid-word is not reported as a half-typed token; its ceiling is what `D-48`'s
 * keystroke-to-squiggle gate leaves after the compile time for the script size. It does not adapt
 * at runtime. 300 ms is `51`'s recorded default and stands as provisional until P5.5's Playwright
 * benchmark measures it on the reference environment; `09` carries the baseline.
 */
export const debounceMs = 300;

/** The delay before the fast `validate` phase fires, when it is enabled (`51` two-phase feedback). */
export const validateDelayMs = 100;

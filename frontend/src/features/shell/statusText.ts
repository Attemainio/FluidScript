import type { Status } from '../../design/primitives/index.ts';
import type { SolveStatus } from '../../state/draftStore.ts';

/** `51`'s four states: a glyph and a word each, distinct without colour (`R-42`, invariant 8c). */
export const statusText: Readonly<
  Record<SolveStatus, { readonly glyph: string; readonly word: string; readonly status: Status }>
> = {
  converging: { glyph: '◐', word: 'Converging', status: 'info' },
  converged: { glyph: '●', word: 'Converged', status: 'ok' },
  failed: { glyph: '▲', word: 'Did not converge', status: 'error' },
  idle: { glyph: '○', word: 'Not solved', status: 'neutral' },
};

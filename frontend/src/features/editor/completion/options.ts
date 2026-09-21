import type { Completion } from '@codemirror/autocomplete';

import type { Item } from './completion.ts';

/**
 * The detail on the option that heads an ambiguous list (`52`, `U-6`): CodeMirror preselects the
 * first option of every list and the setting is global, so where two kinds match within `D-15`'s
 * margin the first option is the typed text itself, which Enter inserts unchanged. The editor then
 * never makes the choice the compiler refuses to make; the reader arrows to one of the pair.
 */
export const ambiguityDetail = 'matches two kinds equally; pick one below';

/** CodeMirror's options for a completion's items, with the ambiguity header where the items ask for it. */
export function toOptions(items: readonly Item[], typed: string): Completion[] {
  const options = items.map(toCompletion);
  if (items.some((item) => item.ambiguous === true)) {
    options.unshift({
      label: typed,
      detail: ambiguityDetail,
      type: 'text',
      boost: 99,
      // Inserting nothing: the typed text stays as it is, and nothing the compiler would refuse is written.
      apply: () => {},
    });
  }
  return options;
}

function toCompletion(item: Item): Completion {
  const completion: Completion = {
    label: item.label,
    type: item.type,
    boost: item.rank / 1000,
  };
  if (item.insert !== undefined && item.insert !== item.label) {
    completion.apply = item.insert;
  }
  if (item.detail !== undefined && item.detail.length > 0) {
    completion.detail = item.detail;
  }
  if (item.info !== undefined) {
    completion.info = item.info;
  }
  return completion;
}

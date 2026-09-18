/** `53`'s level of detail, decided by the pane from the zoom. */
export type Detail = 'symbols' | 'names' | 'values' | 'all';

/** What a zoom shows: no labels below 0.5×, key values and port markers above 1.5×, everything above 3×. */
export function detailFor(zoom: number): Detail {
  if (zoom < 0.5) {
    return 'symbols';
  }
  if (zoom < 1.5) {
    return 'names';
  }
  return zoom < 3 ? 'values' : 'all';
}

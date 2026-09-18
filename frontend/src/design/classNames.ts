/** Joins class names, dropping the empty ones. */
export function join(...names: ReadonlyArray<string | undefined | false>): string {
  return names
    .filter((name): name is string => typeof name === 'string' && name.length > 0)
    .join(' ');
}

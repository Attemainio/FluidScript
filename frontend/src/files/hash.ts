/**
 * A content hash for the file lifecycle (`58`): tells two texts apart cheaply enough to run on every
 * keystroke, since dirtiness is `currentHash !== savedHash`. Two FNV-1a passes with different seeds
 * give 64 bits, which is enough for "is this the text I saved" and for matching a recovery entry to
 * the bytes just opened; it is not a cryptographic digest and is never presented as one.
 */
export function hashOf(text: string): string {
  return pass(text, 0x811c9dc5) + pass(text, 0x050c5d1f);
}

function pass(text: string, seed: number): string {
  let hash = seed >>> 0;
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash.toString(16).padStart(8, '0');
}

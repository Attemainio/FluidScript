/**
 * The text of every open document, outside React state (`51` invariant 1: the script's source of
 * truth is the editor's document, and React never holds a second copy). Until P5.5, the editor is
 * a text area and this map is what a CodeMirror `EditorState` per document will be; the pipeline
 * reads it when it sends, and nothing else does.
 */
const texts = new Map<string, string>();
const revisions = new Map<string, number>();

/** The template a new document starts from (`58`: current-version template text). */
export const templateText = 'fluidscript 1\n\ncircuit plant\n\n';

/** The document's text, or the template for one never edited. */
export function textOf(documentId: string): string {
  return texts.get(documentId) ?? templateText;
}

/** The document's revision, counting edits since it was opened. */
export function revisionOf(documentId: string): number {
  return revisions.get(documentId) ?? 0;
}

/** Records an edit and returns its revision. */
export function setText(documentId: string, text: string): number {
  texts.set(documentId, text);
  const revision = revisionOf(documentId) + 1;
  revisions.set(documentId, revision);
  return revision;
}

/** Forgets a closed document's text. */
export function forget(documentId: string): void {
  texts.delete(documentId);
  revisions.delete(documentId);
}

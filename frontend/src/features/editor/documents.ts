import { EditorState, type Extension } from '@codemirror/state';

/**
 * The editor state of every open document, outside React (`51` invariant 1: the CodeMirror document
 * is the script's only copy). One `EditorState` per document, swapped into the one view on a tab
 * switch, so a background document keeps its text, its undo history and its selection.
 */
const states = new Map<string, EditorState>();
const revisions = new Map<string, number>();
let extensions: Extension = [];

/** The template a new document starts from (`58`: current-version template text). */
export const templateText = 'fluidscript 1\n\ncircuit plant\n\n';

/** Sets the extensions a fresh state is created with; the editor feature calls it once. */
export function configureDocuments(shared: Extension): void {
  extensions = shared;
}

/** The document's editor state, created from the template on first use. */
export function stateOf(documentId: string): EditorState {
  let state = states.get(documentId);
  if (state === undefined) {
    state = EditorState.create({ doc: templateText, extensions });
    states.set(documentId, state);
  }
  return state;
}

/** Records the document's state after a transaction; bumps the revision when the text changed. */
export function updateState(documentId: string, state: EditorState, docChanged: boolean): number {
  states.set(documentId, state);
  if (docChanged) {
    revisions.set(documentId, revisionOf(documentId) + 1);
  }
  return revisionOf(documentId);
}

/** The document's text. */
export function textOf(documentId: string): string {
  return stateOf(documentId).doc.toString();
}

/** The document's revision, counting edits since it was opened. */
export function revisionOf(documentId: string): number {
  return revisions.get(documentId) ?? 0;
}

/** Forgets a closed document. */
export function forget(documentId: string): void {
  states.delete(documentId);
  revisions.delete(documentId);
}

/** Forgets every document; a test's reset. */
export function forgetAll(): void {
  states.clear();
  revisions.clear();
}

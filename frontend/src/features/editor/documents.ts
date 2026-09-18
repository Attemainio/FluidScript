import { Compartment, EditorState, type Extension } from '@codemirror/state';
import { EditorView } from '@codemirror/view';

import { templateText } from '../../files/template.ts';

/**
 * The editor state of every open document, outside React (`51` invariant 1: the CodeMirror document
 * is the script's only copy). One `EditorState` per document, swapped into the one view on a tab
 * switch, so a background document keeps its text, its undo history and its selection.
 */
const states = new Map<string, EditorState>();
const revisions = new Map<string, number>();
const readOnlyCompartment = new Compartment();
let extensions: Extension = [];
const readOnlyExtension: Extension = [EditorState.readOnly.of(true), EditorView.editable.of(false)];

export { templateText };

/** Sets the extensions a fresh state is created with; the editor feature calls it once. */
export function configureDocuments(shared: Extension): void {
  extensions = [shared, readOnlyCompartment.of([])];
}

/**
 * Gives a document text from outside the editor -- a file opened, a draft restored, a reload from
 * disk (`58`) -- as a fresh state, read-only where the file is one this build cannot edit
 * (`FILE005`). The caller swaps it into the view when the document is the active one.
 */
export function loadText(documentId: string, text: string, readOnly = false): EditorState {
  let state = EditorState.create({ doc: text, extensions });
  if (readOnly) {
    state = state.update({ effects: readOnlyCompartment.reconfigure(readOnlyExtension) }).state;
  }
  states.set(documentId, state);
  revisions.set(documentId, revisionOf(documentId) + 1);
  return state;
}

/** Whether the document is read-only (`FILE005`). */
export function isReadOnly(documentId: string): boolean {
  return stateOf(documentId).facet(EditorState.readOnly);
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

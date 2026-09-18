import type { EditorState } from '@codemirror/state';
import type { EditorView } from '@codemirror/view';

let view: EditorView | null = null;
let onChange: ((state: EditorState, docChanged: boolean) => void) | null = null;

/** Registers the one editor view; the pane sets it while mounted. */
export function registerEditorView(editor: EditorView | null): void {
  view = editor;
}

/** The one editor view, for a test that types into it and for commands that need it. */
export function activeEditorView(): EditorView | null {
  return view;
}

/**
 * Sets what an editor transaction does. The document states are created once with an update
 * listener that calls this, so the listener never captures a pane's props: a remounted pane, or a
 * test's new pipeline, takes over every existing document by registering here.
 */
export function registerEditorHandler(
  handler: ((state: EditorState, docChanged: boolean) => void) | null,
): void {
  onChange = handler;
}

/** Called by the update listener on every transaction. */
export function editorChanged(state: EditorState, docChanged: boolean): void {
  onChange?.(state, docChanged);
}

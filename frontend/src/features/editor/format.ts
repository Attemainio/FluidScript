import type { EditorView } from '@codemirror/view';

import type { ApiClient } from '../../api/client.ts';

/**
 * Shift+Alt+F (`52` commands): asks the host for the canonical layout as edits (`17`) and applies
 * them as one transaction, so the cursor stays where the text it was on went and one undo restores
 * the previous layout. Edits are computed on the text sent; if the document changed meanwhile they
 * are dropped rather than applied to the wrong text.
 */
export async function formatDocument(view: EditorView, client: ApiClient): Promise<boolean> {
  const script = view.state.doc.toString();
  let edits;
  try {
    ({ edits } = await client.format(script, new AbortController().signal));
  } catch {
    return false; // 52 invariant 7: a network feature degrades to absent
  }
  if (view.state.doc.toString() !== script || edits.length === 0) {
    return false;
  }
  view.dispatch({
    changes: edits.map((edit) => ({
      from: edit.span.start,
      to: edit.span.start + edit.span.length,
      insert: edit.newText,
    })),
    userEvent: 'format',
  });
  return true;
}

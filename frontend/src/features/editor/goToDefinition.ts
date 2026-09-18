import { EditorView } from '@codemirror/view';

import type { ModelContract } from '../../api/types.ts';

/**
 * Ctrl+Click on a component name jumps to its declaration (`52` commands): the model's
 * `sourceSpan` says where. An inferred component has none, so nothing happens for it.
 */
export function goToDefinition(
  event: MouseEvent,
  view: EditorView,
  model: () => ModelContract | null,
): boolean {
  if (!(event.ctrlKey || event.metaKey)) {
    return false;
  }
  const position = view.posAtCoords({ x: event.clientX, y: event.clientY });
  if (position === null) {
    return false;
  }
  const word = view.state.wordAt(position);
  if (word === null) {
    return false;
  }
  const name = view.state.sliceDoc(word.from, word.to);
  const component = model()?.components.find((c) => c.id === name);
  const span = component?.sourceSpan;
  if (span === null || span === undefined) {
    return false;
  }

  event.preventDefault();
  view.dispatch({
    selection: { anchor: span.start, head: span.start + span.length },
    scrollIntoView: true,
  });
  return true;
}

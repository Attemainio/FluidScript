import { StateEffect, StateField, type EditorState } from '@codemirror/state';
import { Decoration, EditorView, type DecorationSet } from '@codemirror/view';

import type { ModelContract } from '../../api/types.ts';

/**
 * Selection is bidirectional (`54`): placing the caret on a declaration selects the component on
 * the canvas, and selecting on the canvas or in the log scrolls the editor to the declaration and
 * highlights its line. The highlight is a line decoration driven by one effect, so the editor
 * never keeps a second copy of the selection.
 */
export const setHighlight = StateEffect.define<readonly { from: number; to: number }[]>();

const highlightLine = Decoration.line({ class: 'cm-selected-declaration' });

/** The highlighted declaration lines, replaced wholesale by `setHighlight`. */
export const declarationHighlight = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(value, transaction) {
    let next = value.map(transaction.changes);
    for (const effect of transaction.effects) {
      if (effect.is(setHighlight)) {
        const lines = new Set<number>();
        for (const span of effect.value) {
          const at = Math.min(span.from, transaction.state.doc.length);
          lines.add(transaction.state.doc.lineAt(at).from);
        }
        next = Decoration.set(
          [...lines].sort((a, b) => a - b).map((from) => highlightLine.range(from)),
        );
      }
    }
    return next;
  },
  provide: (field) => EditorView.decorations.from(field),
});

/** The declared component whose source span holds the caret, or `null`. */
export function componentAtCaret(state: EditorState, model: ModelContract | null): string | null {
  if (model === null) {
    return null;
  }
  const head = state.selection.main.head;
  const line = state.doc.lineAt(head);
  for (const component of model.components) {
    const span = component.sourceSpan;
    if (span === null) {
      continue;
    }
    // The declaration's line, not only its span: the caret anywhere on the line means that component.
    if (span.start <= line.to && span.start + span.length >= line.from) {
      return component.id;
    }
  }
  return null;
}

/** The source spans of the selected declared components, for the highlight and the scroll. */
export function spansOf(
  model: ModelContract | null,
  ids: readonly string[],
): { from: number; to: number }[] {
  if (model === null) {
    return [];
  }
  return ids.flatMap((id) => {
    const span = model.components.find((c) => c.id === id)?.sourceSpan;
    return span === null || span === undefined
      ? []
      : [{ from: span.start, to: span.start + span.length }];
  });
}

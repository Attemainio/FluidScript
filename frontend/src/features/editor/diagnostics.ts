import { type Diagnostic as LintDiagnostic } from '@codemirror/lint';
import type { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';

import type { Diagnostic } from '../../api/types.ts';

/**
 * Turns the compile's diagnostics (`44`) into CodeMirror's: errors and warnings get a squiggle,
 * infos none (`52` inline diagnostics), the hover shows the message, the code and the related
 * places, and a `suggestion` becomes a quick-fix action that applies as one transaction, which is
 * one undo step (`52` invariant 4).
 */
export function toLintDiagnostics(
  diagnostics: readonly Diagnostic[],
  state: EditorState,
): LintDiagnostic[] {
  const length = state.doc.length;
  const out: LintDiagnostic[] = [];

  for (const diagnostic of diagnostics) {
    if (
      diagnostic.range === null ||
      diagnostic.range === undefined ||
      diagnostic.severity === 'info'
    ) {
      continue;
    }
    const from = Math.min(diagnostic.range.offset, length);
    const to = Math.min(diagnostic.range.offset + diagnostic.range.length, length);
    const suggestion = diagnostic.suggestion;

    out.push({
      from,
      to: Math.max(from, to),
      severity: diagnostic.severity === 'error' ? 'error' : 'warning',
      message: diagnostic.message,
      source: diagnostic.code,
      renderMessage: () => renderMessage(diagnostic),
      actions:
        suggestion === null || suggestion === undefined
          ? []
          : [
              {
                name: 'Apply fix',
                apply: (view: EditorView) => {
                  // Skipped silently when the document moved under it (52 error cases); the next
                  // compile re-offers it.
                  const end = suggestion.range.offset + suggestion.range.length;
                  if (end > view.state.doc.length) {
                    return;
                  }
                  view.dispatch({
                    changes: { from: suggestion.range.offset, to: end, insert: suggestion.newText },
                    userEvent: 'quickfix',
                  });
                },
              },
            ],
    });
  }

  return out;
}

function renderMessage(diagnostic: Diagnostic): HTMLElement {
  const root = document.createElement('div');
  root.className = 'editor-diagnostic';

  const message = document.createElement('div');
  message.textContent = diagnostic.message;
  root.append(message);

  const code = document.createElement('div');
  code.className = 'editor-diagnostic__code';
  code.textContent = diagnostic.code;
  root.append(code);

  for (const related of diagnostic.related ?? []) {
    const line = document.createElement('div');
    line.className = 'editor-diagnostic__related';
    line.textContent = `${related.message} (line ${related.range.start.line + 1})`;
    root.append(line);
  }

  return root;
}

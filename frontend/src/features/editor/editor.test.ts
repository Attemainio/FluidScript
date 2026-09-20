// @vitest-environment jsdom
import { history, redo, undo } from '@codemirror/commands';
import { ensureSyntaxTree } from '@codemirror/language';
import { diagnosticCount, setDiagnostics } from '@codemirror/lint';
import { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { highlightTree } from '@lezer/highlight';
import { describe, expect, it } from 'vitest';

import type { Diagnostic } from '../../api/types.ts';
import { toLintDiagnostics } from './diagnostics.ts';
import {
  fluidscriptHighlighting,
  fluidscriptLanguage,
  highlightStyle,
} from './language/fluidscript.ts';

function diagnostic(partial: Partial<Diagnostic> & { code: string }): Diagnostic {
  return {
    severity: 'error',
    message: partial.code,
    range: null,
    component: null,
    suggestion: null,
    related: [],
    ...partial,
  } as Diagnostic;
}

function range(offset: number, length: number): NonNullable<Diagnostic['range']> {
  return {
    offset,
    length,
    start: { line: 0, character: offset },
    end: { line: 0, character: offset + length },
  } as NonNullable<Diagnostic['range']>;
}

describe('highlighting', () => {
  it('classes every token with 55 syntax names, with no network and no server', () => {
    // 52 invariant 2 and the acceptance row: highlighting is client-side, from the tokenizer.
    const doc =
      'fluidscript 1\nHE1 heat_exchanger power=30kW in.t=20 # coil\nconnections\nN1 - HE1.in\n';
    const state = EditorState.create({
      doc,
      extensions: [fluidscriptLanguage, fluidscriptHighlighting],
    });
    const tree = ensureSyntaxTree(state, doc.length, 1000)!;
    const classes: string[] = [];
    highlightTree(tree, highlightStyle, (from, to, cls) =>
      classes.push(`${doc.slice(from, to)}:${cls}`),
    );

    expect(classes).toEqual([
      'fluidscript:syn-keyword',
      '1:syn-number',
      'HE1:syn-identifier',
      'heat_exchanger:syn-kind',
      'power:syn-parameter',
      '=:syn-operator',
      '30:syn-number',
      'kW:syn-unit',
      'in:syn-parameter',
      '.:syn-operator',
      't:syn-parameter',
      '=:syn-operator',
      '20:syn-number',
      '# coil:syn-comment',
      'connections:syn-keyword',
      'N1:syn-identifier',
      '-:syn-operator',
      'HE1:syn-identifier',
      '.:syn-operator',
      'in:syn-reference',
    ]);
  });
});

describe('inline diagnostics', () => {
  const doc = 'fluidscript 1\nHE1 heat_exchanger powr=30\n';

  it('squiggle errors and warnings, skip infos, and carry the code and the related places', () => {
    const state = EditorState.create({ doc });
    const lint = toLintDiagnostics(
      [
        diagnostic({ code: 'FS1503', severity: 'error', range: range(33, 4) }),
        diagnostic({ code: 'FS1507', severity: 'warning', range: range(14, 3) }),
        diagnostic({ code: 'FS1520', severity: 'info', range: range(14, 3) }),
        diagnostic({ code: 'FS1511', severity: 'error', range: null }),
      ],
      state,
    );

    expect(lint.map((d) => [d.source, d.severity, d.from, d.to])).toEqual([
      ['FS1503', 'error', 33, 37],
      ['FS1507', 'warning', 14, 17],
    ]);
    const rendered = lint[0]!.renderMessage!(new EditorView({ state }));
    expect(rendered.textContent).toContain('FS1503');
  });

  it('applies a quick fix as one transaction, undone in one step', () => {
    // 52 invariant 4 and its acceptance row.
    const view = new EditorView({ state: EditorState.create({ doc, extensions: [history()] }) });
    const [fix] = toLintDiagnostics(
      [
        diagnostic({
          code: 'FS1512',
          range: range(33, 4),
          suggestion: { range: range(33, 4), newText: 'power' } as Diagnostic['suggestion'],
        }),
      ],
      view.state,
    );
    view.dispatch({ selection: { anchor: 5 } });

    fix!.actions![0]!.apply(view, fix!.from, fix!.to);
    expect(view.state.doc.toString()).toBe('fluidscript 1\nHE1 heat_exchanger power=30\n');
    expect(view.state.selection.main.head).toBe(5);

    undo(view);
    expect(view.state.doc.toString()).toBe(doc);
    redo(view);
    expect(view.state.doc.toString()).toContain('power=30');
  });

  it('skips a fix the document has moved from under', () => {
    const view = new EditorView({ state: EditorState.create({ doc: 'short' }) });
    const [fix] = toLintDiagnostics(
      [
        diagnostic({
          code: 'FS1512',
          range: range(0, 3),
          suggestion: { range: range(40, 4), newText: 'x' } as Diagnostic['suggestion'],
        }),
      ],
      view.state,
    );
    fix!.actions![0]!.apply(view, 0, 3);
    expect(view.state.doc.toString()).toBe('short');
  });

  it('keeps squiggles across an edit until the next set replaces them wholesale', () => {
    // 52 invariant 3 and "squiggles persist across the debounce gap".
    const view = new EditorView({ state: EditorState.create({ doc }) });
    view.dispatch(
      setDiagnostics(
        view.state,
        toLintDiagnostics([diagnostic({ code: 'FS1503', range: range(33, 4) })], view.state),
      ),
    );
    expect(diagnosticCount(view.state)).toBe(1);

    view.dispatch({ changes: { from: doc.length, insert: 'N1 node\n' } });
    expect(diagnosticCount(view.state)).toBe(1);

    view.dispatch(setDiagnostics(view.state, []));
    expect(diagnosticCount(view.state)).toBe(0);
  });
});

describe('format', () => {
  it('applies the host edits as one transaction and one undo step', async () => {
    const { formatDocument } = await import('./format.ts');
    const { FakeClient } = await import('../../test/fakes.ts');
    const client = new FakeClient();
    client.formatEdits = () => [
      { span: { start: 14, length: 11 }, newText: 'HE1 pump' },
      { span: { start: 26, length: 10 }, newText: 'PU1 pump' },
    ];
    const view = new EditorView({
      state: EditorState.create({
        doc: 'fluidscript 1\nHE1    pump\nPU1   pump\n',
        extensions: [history()],
      }),
    });
    view.dispatch({ selection: { anchor: 36 } }); // the end of the last line

    expect(await formatDocument(view, client)).toBe(true);
    expect(view.state.doc.toString()).toBe('fluidscript 1\nHE1 pump\nPU1 pump\n');
    expect(view.state.selection.main.head).toBe(31); // the cursor rode along with its text

    undo(view);
    expect(view.state.doc.toString()).toBe('fluidscript 1\nHE1    pump\nPU1   pump\n');
  });

  it('does nothing when the document changed while the host was answering', async () => {
    const { formatDocument } = await import('./format.ts');
    const { FakeClient } = await import('../../test/fakes.ts');
    const client = new FakeClient();
    client.formatEdits = () => [{ span: { start: 0, length: 3 }, newText: 'x' }];
    const view = new EditorView({ state: EditorState.create({ doc: 'a   b' }) });
    const original = client.format.bind(client);
    client.format = async (script, signal) => {
      view.dispatch({ changes: { from: 0, insert: 'z' } });
      return original(script, signal);
    };

    expect(await formatDocument(view, client)).toBe(false);
    expect(view.state.doc.toString()).toBe('za   b');
  });
});

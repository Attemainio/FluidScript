import { acceptCompletion, autocompletion, completionKeymap } from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap, toggleComment } from '@codemirror/commands';
import { setDiagnostics } from '@codemirror/lint';
import { EditorState } from '@codemirror/state';
import {
  drawSelection,
  EditorView,
  highlightActiveLine,
  keymap,
  lineNumbers,
} from '@codemirror/view';
import { useEffect, useRef } from 'react';

import { hashOf } from '../../files/hash.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { editorChanged, registerEditorHandler, registerEditorView } from './activeView.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { usePipeline } from '../pipeline/pipelineContext.ts';
import { useFiles } from '../files/filesContext.ts';
import { fluidscriptCompletion } from './completion/source.ts';
import { formatDocument } from './format.ts';
import { toLintDiagnostics } from './diagnostics.ts';
import { configureDocuments, revisionOf, stateOf, textOf, updateState } from './documents.ts';
import { goToDefinition } from './goToDefinition.ts';
import { editorHover } from './hover.tsx';
import { componentAtCaret, declarationHighlight, setHighlight, spansOf } from './selectionSync.ts';
import { selectionOf, useSelectionStore } from '../../state/selectionStore.ts';
import { fluidscriptHighlighting, fluidscriptLanguage } from './language/fluidscript.ts';
import { debounceMs } from '../pipeline/debounce.ts';

/**
 * The script pane (`52`): one CodeMirror view, one state per document. Every change goes to the
 * document map and restarts the pipeline's debounce; the compile's diagnostics are set wholesale
 * when they arrive and stay until the next ones (`52` invariant 3 and the persistence across the
 * gap); a tab switch swaps the state in and cancels the outgoing document's compile (`51` 8b).
 */
export function EditorPane(): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const setText = useWorkspaceStore((state) => state.setText);
  const pipeline = usePipeline();
  const files = useFiles();
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const current = useRef(documentId);
  current.current = documentId;

  const diagnostics = useDraftStore((state) => draftOf(state, documentId).diagnostics);

  useEffect(() => {
    const element = host.current;
    if (element === null) {
      return;
    }

    const solve = (): boolean => {
      pipeline.flush('solve');
      return true;
    };

    configureDocuments([
      // The name assistive technology reads for the editable region (R-42).
      EditorView.contentAttributes.of({ 'aria-label': 'Script' }),
      lineNumbers(),
      history(),
      drawSelection(),
      highlightActiveLine(),
      fluidscriptLanguage,
      fluidscriptHighlighting,
      autocompletion({ override: [fluidscriptCompletion], activateOnTyping: true, icons: false }),
      keymap.of([
        // Tab commits the selected item (52: Tab inserts the canonical keyword); Enter does too.
        { key: 'Tab', run: acceptCompletion },
        ...completionKeymap,
        { key: 'Mod-/', run: toggleComment },
        { key: 'Mod-Shift-Enter', run: solve },
        // 58's shortcuts, in the editor where the browser would otherwise take them.
        { key: 'Mod-s', run: () => (void files.save(current.current), true) },
        { key: 'Mod-Shift-s', run: () => (void files.saveAs(current.current), true) },
        { key: 'Mod-o', run: () => (void files.openFiles(), true) },
        {
          key: 'Shift-Alt-f',
          run: (editor) => {
            void formatDocument(editor, pipeline.client);
            return true;
          },
        },
        ...defaultKeymap,
        ...historyKeymap,
      ]),
      EditorView.domEventHandlers({
        mousedown: (event, editor) =>
          goToDefinition(
            event,
            editor,
            () => draftOf(useDraftStore.getState(), current.current).model,
          ),
      }),
      EditorView.updateListener.of((update) =>
        editorChanged(update.state, update.docChanged, update.selectionSet && !update.docChanged),
      ),
      declarationHighlight,
      editorHover,
      EditorState.tabSize.of(4),
    ]);

    registerEditorHandler((state, docChanged, caretMoved) => {
      const id = current.current;
      const revision = updateState(id, state, docChanged);
      if (docChanged) {
        setText(id, hashOf(state.doc.toString()));
        pipeline.edit(id, state.doc.toString(), revision);
      }
      if (caretMoved) {
        // The caret on a declaration selects it (54); a caret elsewhere leaves the selection alone.
        const model = draftOf(useDraftStore.getState(), id).model;
        const at = componentAtCaret(state, model);
        const store = useSelectionStore.getState();
        const selected = selectionOf(store, id);
        if (at !== null && (selected.length !== 1 || selected[0] !== at)) {
          store.select(id, at, 'editor');
        }
      }
    });
    const created = new EditorView({ state: stateOf(documentId), parent: element });
    view.current = created;
    registerEditorView(created);

    if (import.meta.env.DEV) {
      // The benchmark's hook (D-48, e2e/latency.bench.ts): the dev server only, never the build.
      const setText = (text: string): void =>
        created.dispatch({ changes: { from: 0, to: created.state.doc.length, insert: text } });
      (window as Window & { fluidscript?: unknown }).fluidscript = {
        setText,
        text: () => created.state.doc.toString(),
        timings: () => draftOf(useDraftStore.getState(), current.current).timings,
        debounceMs,
      };
      // `?script=` loads text on start, so a headless screenshot can show a real document.
      const script = new URLSearchParams(window.location.search).get('script');
      if (script !== null) {
        setText(script);
      }
    }

    // A selection made elsewhere scrolls the editor to the declaration; one made here only highlights.
    const unsubscribe = useSelectionStore.subscribe((store) => {
      const id = current.current;
      const model = draftOf(useDraftStore.getState(), id).model;
      const spans = spansOf(model, selectionOf(store, id));
      const first = spans[0];
      created.dispatch({
        effects: [
          setHighlight.of(spans),
          ...(first !== undefined && store.origin[id] !== 'editor'
            ? [EditorView.scrollIntoView(first.from, { y: 'center' })]
            : []),
        ],
      });
    });

    return () => {
      unsubscribe();
      created.destroy();
      view.current = null;
      registerEditorView(null);
      registerEditorHandler(null);
    };
    // The view is created once; documents are swapped into it below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const editor = view.current;
    if (editor === null) {
      return;
    }
    editor.setState(stateOf(documentId));
    pipeline.edit(documentId, textOf(documentId), revisionOf(documentId));
    return () => pipeline.cancel(documentId);
  }, [documentId, pipeline]);

  useEffect(() => {
    const editor = view.current;
    if (editor === null) {
      return;
    }
    editor.dispatch(setDiagnostics(editor.state, toLintDiagnostics(diagnostics, editor.state)));
  }, [diagnostics, documentId]);

  return <div ref={host} className="editor-pane" aria-label="Script" />;
}

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

import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { editorChanged, registerEditorHandler, registerEditorView } from './activeView.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { usePipeline } from '../pipeline/pipelineContext.ts';
import { fluidscriptCompletion } from './completion/source.ts';
import { formatDocument } from './format.ts';
import { toLintDiagnostics } from './diagnostics.ts';
import { configureDocuments, revisionOf, stateOf, textOf, updateState } from './documents.ts';
import { goToDefinition } from './goToDefinition.ts';
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
  const setDirty = useWorkspaceStore((state) => state.setDirty);
  const pipeline = usePipeline();
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
      EditorView.updateListener.of((update) => editorChanged(update.state, update.docChanged)),
      EditorState.tabSize.of(4),
    ]);

    registerEditorHandler((state, docChanged) => {
      const id = current.current;
      const revision = updateState(id, state, docChanged);
      if (docChanged) {
        setDirty(id, true);
        pipeline.edit(id, state.doc.toString(), revision);
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

    return () => {
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

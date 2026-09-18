import { useEffect, useMemo } from 'react';

import { selectBackend, type FileBackend } from '../../files/backend.ts';
import { FileActions, type TextAccess } from '../../files/fileActions.ts';
import { useFileStore } from '../../files/fileStore.ts';
import { hashOf } from '../../files/hash.ts';
import { selectRecoveryStore, type RecoveryStore } from '../../files/recovery.ts';
import { RecoveryScheduler, type Clock } from '../../files/scheduler.ts';
import { templateText } from '../../files/template.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { useRunStore } from '../../state/runStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { activeEditorView, editorChanged } from '../editor/activeView.ts';
import { forget, loadText, revisionOf, stateOf, textOf, updateState } from '../editor/documents.ts';
import { usePipeline } from '../pipeline/pipelineContext.ts';
import { FilesContext } from './filesContext.ts';

const realClock: Clock = {
  now: () => Date.now(),
  setTimeout: (callback, ms) => setTimeout(callback, ms),
  clearTimeout: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/** The editor's text as the file actions see it: the document map, swapped into the view when active. */
function textAccess(): TextAccess {
  return {
    textOf,
    loadText: (documentId, text, readOnly) => {
      const state = loadText(documentId, text, readOnly);
      const view = activeEditorView();
      if (view !== null && useWorkspaceStore.getState().activeDocumentId === documentId) {
        view.setState(state);
        editorChanged(state, true);
      }
      return state;
    },
    diagnosticsOf: (documentId) => {
      const draft = draftOf(useDraftStore.getState(), documentId);
      return {
        diagnostics: draft.diagnostics,
        current: draft.diagnosticsRevision >= revisionOf(documentId),
      };
    },
    applySuggestion: (documentId, diagnostic) => {
      const suggestion = diagnostic.suggestion;
      if (suggestion === null || suggestion === undefined) {
        return;
      }
      const change = {
        from: suggestion.range.offset,
        to: suggestion.range.offset + suggestion.range.length,
        insert: suggestion.newText,
      };
      const view = activeEditorView();
      if (view !== null && useWorkspaceStore.getState().activeDocumentId === documentId) {
        view.dispatch({ changes: change });
      } else {
        const state = stateOf(documentId).update({ changes: change }).state;
        updateState(documentId, state, true);
        useWorkspaceStore.getState().setText(documentId, hashOf(state.doc.toString()));
      }
    },
    forget: (documentId) => {
      useDraftStore.getState().dispose(documentId);
      useRunStore.getState().dispose(documentId);
      forget(documentId);
    },
  };
}

/**
 * Makes `58`'s file actions available to the shell: the backend this browser supports, the
 * recovery store, the recovery timer, and the launch pass that finds each remembered document's
 * text again. A test passes its own backend, store and clock.
 */
export function FilesProvider({
  backend,
  recovery,
  clock,
  children,
}: {
  readonly backend?: FileBackend;
  readonly recovery?: RecoveryStore;
  readonly clock?: Clock;
  readonly children: React.ReactNode;
}): React.ReactNode {
  const pipeline = usePipeline();
  const actions = useMemo(() => {
    const own = backend ?? selectBackend(window);
    const store = recovery ?? selectRecoveryStore(window);
    const ticker = clock ?? realClock;
    let created: FileActions | null = null;
    const scheduler = new RecoveryScheduler(
      ticker,
      (documentId) => void created?.writeRecovery(documentId),
    );
    created = new FileActions({
      backend: own,
      recovery: store,
      scheduler,
      client: pipeline.client,
      text: textAccess(),
      workspace: useWorkspaceStore,
      files: useFileStore,
      runs: useRunStore,
      now: () => ticker.now(),
    });
    useFileStore.getState().setCanOverwrite(own.canOverwrite);
    return { actions: created, scheduler };
  }, [backend, recovery, clock, pipeline]);

  useEffect(() => {
    void actions.actions.start();
  }, [actions]);

  // Every edit arms the document's recovery timer (58: 1 s idle, at most every 5 s).
  useEffect(
    () =>
      useWorkspaceStore.subscribe((state, previous) => {
        for (const doc of state.documents) {
          const before = previous.documents.find((d) => d.documentId === doc.documentId);
          if (before !== undefined && before.currentHash !== doc.currentHash && doc.dirty) {
            actions.scheduler.noteEdit(doc.documentId);
          }
        }
      }),
    [actions],
  );

  // Leaving with unsaved text asks first (58: navigating away with dirty text prompts).
  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent): void => {
      const unsaved = useWorkspaceStore
        .getState()
        .documents.some((d) => d.loaded && d.dirty && textOf(d.documentId) !== templateText);
      if (unsaved) {
        event.preventDefault();
      }
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, []);

  return <FilesContext value={actions.actions}>{children}</FilesContext>;
}

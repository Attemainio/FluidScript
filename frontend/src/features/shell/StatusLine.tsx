import { StatusDot } from '../../design/primitives/index.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { useRunStore } from '../../state/runStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { statusText } from './statusText.ts';

/**
 * One line, always present, naming the state, the computation and the document it describes
 * (`51` status line, `R-51`): `● Converged · steady solve · plant_01`. It describes the active
 * document only; a background run is reachable from its tab's marker.
 */
export function StatusLine(): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const name = useWorkspaceStore(
    (state) => state.documents.find((d) => d.documentId === documentId)?.displayName ?? '',
  );
  const draft = useDraftStore((state) => draftOf(state, documentId));
  const run = useRunStore((state) => state.runs[documentId]);

  const running = run?.status === 'running';
  const state = running ? statusText.converging : statusText[draft.status];
  const computation = running
    ? 'transient run'
    : draft.compiling !== null
      ? 'draft compile'
      : 'steady solve';
  const text = `${state.glyph} ${state.word} · ${computation} · ${name}`;

  return (
    <footer className="status-line">
      <div role="status" aria-live="polite" className="status-line__state">
        <StatusDot status={state.status} label={text} />
        {draft.fault !== null && (
          <span className="status-line__fault">
            {draft.fault.status === 0 ? 'Not connected' : `Request failed (${draft.fault.status})`}
            {draft.fault.correlationId !== undefined && ` · ${draft.fault.correlationId}`}
          </span>
        )}
      </div>
    </footer>
  );
}

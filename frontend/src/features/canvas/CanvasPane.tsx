import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';

/**
 * The drawing pane. P5.6 draws `layout.placements` and `layout.routes` here; until then it shows
 * what the last successful model holds, which is enough to see the pipeline keep it through a
 * failed compile (`51` invariant 2).
 */
export function CanvasPane(): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const draft = useDraftStore((state) => draftOf(state, documentId));

  if (draft.model === null) {
    return (
      <div className="canvas-pane" aria-label="Diagram">
        <p className="canvas-pane__empty">No model yet.</p>
      </div>
    );
  }

  const model = draft.model;
  return (
    <div className="canvas-pane" aria-label="Diagram">
      <p className="canvas-pane__summary">
        {model.components.length} components · {model.connections.length} connections ·{' '}
        {model.circuits.length} {model.circuits.length === 1 ? 'circuit' : 'circuits'} ·{' '}
        {model.layout.placements.length} placed
      </p>
      <ul className="canvas-pane__list">
        {model.components.map((component) => (
          <li key={component.id}>
            <span className="fs-mono">{component.id}</span> {component.kind}
          </li>
        ))}
      </ul>
    </div>
  );
}

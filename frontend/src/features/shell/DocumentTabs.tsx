import { IconButton, Tabs } from '../../design/primitives/index.ts';
import { useRunStore } from '../../state/runStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { useFiles } from '../files/filesContext.ts';

/**
 * The tab strip (`51`, `D-39`): a tab per document with its dirty marker, a running marker when it
 * owns a run, and New and Close. Closing goes through `58`'s questions -- the run, then the text.
 */
export function DocumentTabs(): React.ReactNode {
  const documents = useWorkspaceStore((state) => state.documents);
  const active = useWorkspaceStore((state) => state.activeDocumentId);
  const activate = useWorkspaceStore((state) => state.activate);
  const runs = useRunStore((state) => state.runs);
  const files = useFiles();

  const tabs = documents.map((d) => ({
    id: d.documentId,
    label: runs[d.documentId]?.status === 'running' ? `${d.displayName} ▸` : d.displayName,
    dirty: d.dirty,
  }));

  const onClose = (): void => void files.closeDocument(active);

  return (
    <div className="document-tabs">
      <Tabs tabs={tabs} selected={active} onSelect={activate} />
      <IconButton label="New document" onClick={() => files.newDocument()}>
        +
      </IconButton>
      <IconButton label="Close document" onClick={onClose}>
        ×
      </IconButton>
    </div>
  );
}

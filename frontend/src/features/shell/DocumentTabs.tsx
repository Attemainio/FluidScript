import { IconButton, Tabs } from '../../design/primitives/index.ts';
import { useDraftStore } from '../../state/draftStore.ts';
import { useRunStore } from '../../state/runStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { forget } from '../editor/documents.ts';

/**
 * The tab strip (`51`, `D-39`): a tab per document with its dirty marker, a running marker when it
 * owns a run, and New and Close. File prompts on close are `58`'s, P5.9; closing here is immediate.
 */
export function DocumentTabs(): React.ReactNode {
  const documents = useWorkspaceStore((state) => state.documents);
  const active = useWorkspaceStore((state) => state.activeDocumentId);
  const activate = useWorkspaceStore((state) => state.activate);
  const open = useWorkspaceStore((state) => state.open);
  const close = useWorkspaceStore((state) => state.close);
  const runs = useRunStore((state) => state.runs);
  const disposeDraft = useDraftStore((state) => state.dispose);
  const disposeRun = useRunStore((state) => state.dispose);

  const tabs = documents.map((d) => ({
    id: d.documentId,
    label: runs[d.documentId]?.status === 'running' ? `${d.displayName} ▸` : d.displayName,
    dirty: d.dirty,
  }));

  const onClose = (): void => {
    const closing = active;
    close(closing);
    disposeDraft(closing);
    disposeRun(closing);
    forget(closing);
  };

  return (
    <div className="document-tabs">
      <Tabs tabs={tabs} selected={active} onSelect={activate} />
      <IconButton label="New document" onClick={() => open()}>
        +
      </IconButton>
      <IconButton label="Close document" onClick={onClose}>
        ×
      </IconButton>
    </div>
  );
}

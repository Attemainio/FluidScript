import { Menu, type MenuItem } from '../../design/primitives/index.ts';
import { useFileStore } from '../../files/fileStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { useFiles } from './filesContext.ts';

/** `58`'s file commands as one menu: New, Open, Save, Save As, Download, and the recovered drafts. */
export function FileMenu(): React.ReactNode {
  const files = useFiles();
  const canOverwrite = useFileStore((state) => state.canOverwrite);
  const active = useWorkspaceStore((state) => state.activeDocumentId);
  const document = useWorkspaceStore((state) =>
    state.documents.find((d) => d.documentId === active),
  );
  const readOnly = document?.readOnly === true;

  const items: MenuItem[] = [
    { id: 'new', label: 'New', onSelect: () => files.newDocument() },
    { id: 'open', label: 'Open…', shortcut: 'Ctrl+O', onSelect: () => void files.openFiles() },
    ...(canOverwrite
      ? [
          {
            id: 'save',
            label: 'Save',
            shortcut: 'Ctrl+S',
            disabled: readOnly,
            reason: 'This file is a version this build cannot edit.',
            onSelect: () => void files.save(active),
          },
          {
            id: 'save-as',
            label: 'Save As…',
            shortcut: 'Ctrl+Shift+S',
            disabled: readOnly,
            reason: 'This file is a version this build cannot edit.',
            onSelect: () => void files.saveAs(active),
          },
        ]
      : []),
    {
      id: 'download',
      label: 'Download .fluid',
      ...(canOverwrite ? {} : { shortcut: 'Ctrl+S' }),
      onSelect: () => files.download(active),
    },
    { id: 'drafts', label: 'Recovered drafts…', onSelect: () => void showDrafts(files) },
  ];

  return <Menu label="File" items={items} className="file-menu" />;
}

/** Lists the drafts that belong to no open tab (`58`: stale ones marked, none deleted unasked). */
async function showDrafts(files: ReturnType<typeof useFiles>): Promise<void> {
  const drafts = await files.listDrafts();
  const answer = await useFileStore.getState().ask({
    kind: 'drafts',
    documentId: null,
    title: 'Recovered drafts',
    message:
      drafts.length === 0
        ? 'No drafts are waiting. A draft appears here when a tab was closed by the browser before its text was saved.'
        : 'Drafts the browser kept of documents that are not open. Restore one into a new tab, download it, or discard it. Nothing here is deleted unasked.',
    choices: [{ id: 'close', label: 'Close', primary: true }],
    drafts,
  });
  void answer;
}

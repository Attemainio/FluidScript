import { Button } from '../../design/primitives/index.ts';
import { useFileStore, type FileNotice } from '../../files/fileStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { useFiles } from './filesContext.ts';

/**
 * The banner over the active document's editor (`58`): what happened with its file and what can
 * be done about it, as text and buttons; a polite live region, so a screen reader hears it.
 */
export function FileNoticeBar(): React.ReactNode {
  const files = useFiles();
  const active = useWorkspaceStore((state) => state.activeDocumentId);
  const notice = useFileStore((state) => state.notices[active]);
  const storeUnavailable = useFileStore((state) => state.storeUnavailable);
  const dismiss = useFileStore((state) => state.dismiss);

  return (
    <div className="file-notices" aria-live="polite">
      {storeUnavailable ? (
        <div className="file-notice file-notice--warning" data-kind="FILE004">
          <span>
            The browser's recovery store is unavailable: unsaved text will not survive a reload.
            Download to keep a copy.
          </span>
          <Button onClick={() => files.download(active)}>Download .fluid</Button>
        </div>
      ) : null}
      {notice !== undefined ? (
        <div className={`file-notice file-notice--${tone(notice)}`} data-kind={notice.kind}>
          <span>{notice.message}</span>
          <span className="file-notice__actions">
            {actionsFor(notice, files, active).map((action) => (
              <Button
                key={action.label}
                onClick={action.run}
                variant={action.primary === true ? 'primary' : 'default'}
              >
                {action.label}
              </Button>
            ))}
            <Button variant="quiet" onClick={() => dismiss(active)} aria-label="Dismiss">
              ×
            </Button>
          </span>
        </div>
      ) : null}
    </div>
  );
}

function tone(notice: FileNotice): 'error' | 'warning' | 'info' {
  switch (notice.kind) {
    case 'FILE001':
    case 'FILE002':
    case 'lost':
      return 'error';
    case 'FILE003':
    case 'FILE005':
    case 'divergent':
    case 'reopen':
    case 'unversioned':
      return 'warning';
    default:
      return 'info';
  }
}

function actionsFor(
  notice: FileNotice,
  files: ReturnType<typeof useFiles>,
  documentId: string,
): readonly { readonly label: string; readonly run: () => void; readonly primary?: boolean }[] {
  switch (notice.kind) {
    case 'FILE001':
    case 'FILE002':
      return [
        ...(notice.other === undefined
          ? [
              { label: 'Save As…', run: () => void files.saveAs(documentId) },
              { label: 'Download .fluid', run: () => files.download(documentId) },
            ]
          : [
              {
                label: 'Restore draft',
                run: () => files.restoreWithoutFile(documentId),
                primary: true,
              },
              { label: 'Download draft', run: () => downloadOther(notice) },
            ]),
      ];
    case 'FILE003':
      return [
        { label: 'Reload from disk', run: () => void files.reloadFromDisk(documentId) },
        { label: 'Save As…', run: () => void files.saveAs(documentId), primary: true },
        { label: 'Compare', run: () => void files.compare(documentId) },
      ];
    case 'unversioned':
      return [
        {
          label: "Add 'fluidscript 1'",
          run: () => files.addVersionLine(documentId),
          primary: true,
        },
      ];
    case 'reopen':
      return [
        { label: 'Reopen file', run: () => void files.reopen(documentId), primary: true },
        ...(notice.other === undefined
          ? []
          : [
              { label: 'Restore draft', run: () => files.restoreWithoutFile(documentId) },
              { label: 'Download draft', run: () => downloadOther(notice) },
            ]),
      ];
    case 'divergent':
      return [
        { label: 'Restore draft', run: () => files.restoreDraft(documentId), primary: true },
        { label: 'Use file', run: () => files.useFile(documentId) },
        { label: 'Compare', run: () => void files.compare(documentId) },
      ];
    case 'FILE005':
      return [{ label: 'Download .fluid', run: () => files.download(documentId) }];
    default:
      return [];
  }
}

function downloadOther(notice: FileNotice): void {
  const url = URL.createObjectURL(new Blob([notice.other?.text ?? ''], { type: 'text/plain' }));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = 'draft.fluid';
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

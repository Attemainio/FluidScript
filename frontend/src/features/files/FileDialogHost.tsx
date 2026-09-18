import { Button, Dialog } from '../../design/primitives/index.ts';
import { useFileStore } from '../../files/fileStore.ts';
import { useFiles } from './filesContext.ts';

/** Shows the file store's one open dialog: a question, a comparison, or the list of drafts. */
export function FileDialogHost(): React.ReactNode {
  const dialog = useFileStore((state) => state.dialog);
  const files = useFiles();
  if (dialog === null) {
    return null;
  }
  return (
    <Dialog
      title={dialog.title}
      message={dialog.message}
      choices={dialog.choices}
      onChoose={dialog.choose}
    >
      {dialog.compare !== undefined ? (
        <div className="file-compare">
          {[dialog.compare.left, dialog.compare.right].map((side) => (
            <section key={side.label} className="file-compare__side">
              <h3>{side.label}</h3>
              <pre>{side.text}</pre>
            </section>
          ))}
        </div>
      ) : null}
      {dialog.drafts !== undefined && dialog.drafts.length > 0 ? (
        <ul className="file-drafts">
          {dialog.drafts.map((draft) => (
            <li key={draft.documentId} className="file-drafts__item">
              <span>
                <strong>{draft.displayName}</strong> · saved {draft.age}
                {draft.stale ? ' · older than 30 days' : ''}
              </span>
              <span className="file-drafts__actions">
                <Button
                  onClick={() => {
                    dialog.choose('restore');
                    void files.restoreDraftToTab(draft);
                  }}
                >
                  Restore
                </Button>
                <Button onClick={() => files.downloadDraft(draft)}>Download</Button>
                <Button
                  onClick={() => {
                    dialog.choose('discard');
                    void files.discardDraft(draft);
                  }}
                >
                  Discard
                </Button>
              </span>
            </li>
          ))}
        </ul>
      ) : null}
    </Dialog>
  );
}

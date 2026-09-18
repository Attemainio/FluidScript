import { useEffect, useState } from 'react';

import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { usePipeline } from '../pipeline/pipelineContext.ts';
import { revisionOf, setText, textOf } from './documents.ts';

/**
 * The script pane. A text area stands in for CodeMirror until P5.5; what it already does is the
 * shell's part: every keystroke goes to the document map and restarts the pipeline's debounce, a
 * tab switch mounts the incoming document's text and cancels the outgoing one's compile (`51` 8b),
 * and the first edit marks the document dirty.
 */
export function EditorPane(): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  return <DocumentEditor key={documentId} documentId={documentId} />;
}

function DocumentEditor({ documentId }: { readonly documentId: string }): React.ReactNode {
  const setDirty = useWorkspaceStore((state) => state.setDirty);
  const pipeline = usePipeline();
  const [text, setLocalText] = useState(() => textOf(documentId));

  useEffect(() => {
    // The incoming document compiles at its current revision so the canvas shows it.
    pipeline.edit(documentId, textOf(documentId), revisionOf(documentId));
    return () => pipeline.cancel(documentId);
  }, [documentId, pipeline]);

  const onChange = (event: React.ChangeEvent<HTMLTextAreaElement>): void => {
    const value = event.target.value;
    setLocalText(value);
    const revision = setText(documentId, value);
    setDirty(documentId, true);
    pipeline.edit(documentId, value, revision);
  };

  return (
    <textarea
      className="editor-pane fs-mono"
      aria-label="Script"
      spellCheck={false}
      value={text}
      onChange={onChange}
    />
  );
}

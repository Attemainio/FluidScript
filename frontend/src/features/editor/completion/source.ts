import type { CompletionContext, CompletionResult } from '@codemirror/autocomplete';

import { draftOf, useDraftStore } from '../../../state/draftStore.ts';
import { useMetadataStore } from '../../../state/metadataStore.ts';
import { useWorkspaceStore } from '../../../state/workspaceStore.ts';
import { complete } from './completion.ts';
import { toOptions } from './options.ts';

/**
 * CodeMirror's completion source over `complete`: the metadata from its store, the model from the
 * active document's draft. Returns nothing rather than throwing when either is missing.
 */
export function fluidscriptCompletion(context: CompletionContext): CompletionResult | null {
  const metadata = useMetadataStore.getState().metadata;
  if (metadata === null) {
    return null;
  }
  const documentId = useWorkspaceStore.getState().activeDocumentId;
  const model = draftOf(useDraftStore.getState(), documentId).model;
  const result = complete(
    { doc: context.state.doc.toString(), offset: context.pos },
    { metadata, model },
  );

  if (result.items.length === 0) {
    return null;
  }
  if (
    !context.explicit &&
    result.from === context.pos &&
    result.context !== 'value' &&
    result.context !== 'port' &&
    result.context !== 'property' &&
    result.context !== 'connection-name'
  ) {
    // Only after a typed character, except where a dot or an equals sign just opened a position.
    return null;
  }

  return {
    from: result.from,
    options: toOptions(result.items, context.state.doc.sliceString(result.from, context.pos)),
    validFor: /^[\w{}.]*$/,
  };
}

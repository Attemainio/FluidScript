import type { Completion, CompletionContext, CompletionResult } from '@codemirror/autocomplete';

import { draftOf, useDraftStore } from '../../../state/draftStore.ts';
import { useMetadataStore } from '../../../state/metadataStore.ts';
import { useWorkspaceStore } from '../../../state/workspaceStore.ts';
import { complete, type Item } from './completion.ts';

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
    options: result.items.map(toCompletion),
    validFor: /^[\w{}.]*$/,
  };
}

function toCompletion(item: Item): Completion {
  const completion: Completion = {
    label: item.label,
    type: item.type,
    boost: item.rank / 1000,
  };
  if (item.insert !== undefined && item.insert !== item.label) {
    completion.apply = item.insert;
  }
  if (item.detail !== undefined && item.detail.length > 0) {
    completion.detail = item.detail;
  }
  if (item.info !== undefined) {
    completion.info = item.info;
  }
  return completion;
}

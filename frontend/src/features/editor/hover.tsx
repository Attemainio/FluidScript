import { hoverTooltip, type EditorView, type Tooltip } from '@codemirror/view';
import { createRoot, type Root } from 'react-dom/client';

import type { Metadata, ModelContract } from '../../api/types.ts';
import { bindingCard, componentCard, type Card } from '../hover/card.ts';
import { HoverCard } from '../hover/HoverCard.tsx';
import { lexLine } from './language/tokenizer.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { useMetadataStore } from '../../state/metadataStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';

/**
 * The editor's hovers (`52` Hover, deferred from P5.5 to here): a component name shows the same
 * card the canvas shows, a `let` name its binding, a quantity what its unit means. The card is the
 * canvas's component rendered into the tooltip's element, so there is one implementation.
 */
export const editorHover = hoverTooltip(
  (view, pos) => {
    const documentId = useWorkspaceStore.getState().activeDocumentId;
    const draft = draftOf(useDraftStore.getState(), documentId);
    const metadata = useMetadataStore.getState().metadata;
    const hit = tokenAt(view, pos);
    if (hit === null) {
      return null;
    }
    const card = cardFor(hit, draft.model, draft.diagnostics, metadata);
    if (card === null) {
      return null;
    }
    return {
      pos: hit.from,
      end: hit.to,
      above: true,
      create: () => mount(card),
    };
  },
  { hoverTime: 150 },
);

interface Hit {
  readonly from: number;
  readonly to: number;
  readonly text: string;
  readonly kind: 'Identifier' | 'QuantityLiteral' | 'NumberLiteral' | 'other';
}

function tokenAt(view: EditorView, pos: number): Hit | null {
  const line = view.state.doc.lineAt(pos);
  const column = pos - line.from;
  for (const token of lexLine(line.text)) {
    if (token.from <= column && column < token.to) {
      const kind =
        token.kind === 'Identifier' ||
        token.kind === 'QuantityLiteral' ||
        token.kind === 'NumberLiteral'
          ? token.kind
          : 'other';
      return { from: line.from + token.from, to: line.from + token.to, text: token.text, kind };
    }
  }
  return null;
}

function cardFor(
  hit: Hit,
  model: ModelContract | null,
  diagnostics: readonly ModelContract['diagnostics'][number][],
  metadata: Metadata | null,
): Card | null {
  if (hit.kind === 'Identifier') {
    return componentCard(model, hit.text, diagnostics) ?? bindingCard(model, hit.text);
  }
  if (hit.kind === 'QuantityLiteral' && metadata !== null) {
    const symbol = hit.text.replace(/^[\d.eE+-]+\s*/, '');
    const dimension = metadata.dimensions.find((d) => d.units.includes(symbol));
    if (dimension === undefined) {
      return null;
    }
    return {
      title: hit.text,
      subtitle: `${dimension.name} · canonical ${dimension.canonicalUnit ?? dimension.siUnit}`,
      inferred: false,
      parameters: [],
      state: [],
      warnings: [],
      note: null,
    };
  }
  return null;
}

/** Renders the card into the tooltip's element with React, and unmounts it when the tooltip goes. */
function mount(card: Card): ReturnType<Tooltip['create']> {
  const dom = document.createElement('div');
  dom.className = 'fs-tooltip fs-tooltip--editor';
  let root: Root | null = createRoot(dom);
  root.render(<HoverCard card={card} />);
  return {
    dom,
    destroy: () => {
      const r = root;
      root = null;
      // React refuses a synchronous unmount from inside a render; the next tick is fine.
      setTimeout(() => r?.unmount(), 0);
    },
  };
}

import { create } from 'zustand';

import type { ApiClient } from '../api/client.ts';
import type { Metadata } from '../api/types.ts';

/** The language's static description (`42`), fetched once and cached; `null` until it arrives or when it cannot. */
export interface MetadataState {
  readonly metadata: Metadata | null;
  readonly load: (client: ApiClient) => Promise<void>;
}

export const useMetadataStore = create<MetadataState>()((set, get) => ({
  metadata: null,
  load: async (client) => {
    if (get().metadata !== null) {
      return;
    }
    try {
      set({ metadata: await client.metadata(new AbortController().signal) });
    } catch {
      // 52 error cases: /metadata unavailable, completion is empty, everything else works.
    }
  },
}));

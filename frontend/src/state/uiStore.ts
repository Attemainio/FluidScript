import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import type { BuiltinThemeName } from '../design/builtin.ts';
import type { ThemeColors } from '../design/tokens.ts';
import type { LogFilter } from '../features/log/logModel.ts';

/**
 * What the user chose for the theme (`55` Theming): follow the system, one of the two built-ins, or
 * a custom file they loaded, kept whole so it survives a reload without the file.
 */
export type ThemeChoice =
  | { readonly kind: 'system' }
  | { readonly kind: 'builtin'; readonly name: BuiltinThemeName }
  | { readonly kind: 'custom'; readonly name: string; readonly colors: ThemeColors };

/** The global UI store (`51`): theme, the split, the log's fold; the canvas viewport when P5.6 lands. */
export interface UiState {
  readonly theme: ThemeChoice;
  /** The editor's share of the width, 0.2..0.8. */
  readonly splitRatio: number;
  readonly logOpen: boolean;
  /** `56`'s filter; warnings and errors by default. */
  readonly logFilter: LogFilter;
  readonly setTheme: (theme: ThemeChoice) => void;
  readonly setSplitRatio: (ratio: number) => void;
  readonly setLogOpen: (open: boolean) => void;
  readonly setLogFilter: (filter: LogFilter) => void;
}

/**
 * The one global UI store, persisted to localStorage. When storage is unavailable the store still
 * works and nothing persists, which is `51`'s error case for it.
 */
export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      theme: { kind: 'system' },
      splitRatio: 0.45,
      logOpen: true,
      logFilter: 'warnings',
      setTheme: (theme) => set({ theme }),
      setSplitRatio: (ratio) => set({ splitRatio: Math.min(0.8, Math.max(0.2, ratio)) }),
      setLogOpen: (open) => set({ logOpen: open }),
      setLogFilter: (filter) => set({ logFilter: filter }),
    }),
    {
      name: 'fluidscript.ui',
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({
        theme: state.theme,
        splitRatio: state.splitRatio,
        logOpen: state.logOpen,
        logFilter: state.logFilter,
      }),
    },
  ),
);

import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';

import type { BuiltinThemeName } from '../design/builtin.ts';
import type { ThemeColors } from '../design/tokens.ts';

/**
 * What the user chose for the theme (`55` Theming): follow the system, one of the two built-ins, or
 * a custom file they loaded, kept whole so it survives a reload without the file.
 */
export type ThemeChoice =
  | { readonly kind: 'system' }
  | { readonly kind: 'builtin'; readonly name: BuiltinThemeName }
  | { readonly kind: 'custom'; readonly name: string; readonly colors: ThemeColors };

/** The global UI store (`51`): theme now; panel sizes and the viewport when their packages land. */
export interface UiState {
  readonly theme: ThemeChoice;
  readonly setTheme: (theme: ThemeChoice) => void;
}

/**
 * The one global UI store, persisted to localStorage. When storage is unavailable the store still
 * works and nothing persists, which is `51`'s error case for it.
 */
export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      theme: { kind: 'system' },
      setTheme: (theme) => set({ theme }),
    }),
    {
      name: 'fluidscript.ui',
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({ theme: state.theme }),
    },
  ),
);

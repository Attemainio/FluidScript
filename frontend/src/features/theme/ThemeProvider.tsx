import { useEffect } from 'react';

import { applyTheme } from '../../design/theme.ts';
import { useUiStore } from '../../state/uiStore.ts';

/**
 * Keeps the document root's theme in step with the UI store. A built-in choice is an attribute the
 * generated stylesheet answers; `system` clears it so `prefers-color-scheme` decides; a custom
 * theme is applied inline. No reload, and nothing below the root re-mounts (`55` invariant 5).
 */
export function ThemeProvider({
  children,
}: {
  readonly children: React.ReactNode;
}): React.ReactNode {
  const theme = useUiStore((state) => state.theme);

  useEffect(() => {
    const root = document.documentElement;
    switch (theme.kind) {
      case 'system':
        applyTheme(root, { name: null });
        break;
      case 'builtin':
        applyTheme(root, { name: theme.name });
        break;
      case 'custom':
        applyTheme(root, { name: theme.name, colors: theme.colors });
        break;
    }
  }, [theme]);

  return children;
}

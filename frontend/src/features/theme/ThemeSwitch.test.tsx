// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import App from '../../App.tsx';
import { useUiStore } from '../../state/uiStore.ts';

// React's act() needs this flag to know a test environment is driving it.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the theme switch', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    localStorage.clear();
    useUiStore.setState({ theme: { kind: 'system' } });
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    act(() => root.render(<App />));
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    delete document.documentElement.dataset['theme'];
  });

  it('puts the chosen built-in on the document root without a reload', () => {
    // 55 invariant 5. The provider sets an attribute; nothing under it re-mounts.
    const select = container.querySelector<HTMLSelectElement>('select[aria-label="Theme"]');
    expect(select).not.toBeNull();
    const preview = container.querySelector('.theme-preview');

    act(() => {
      select!.value = 'dark';
      select!.dispatchEvent(new Event('change', { bubbles: true }));
    });

    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(container.querySelector('.theme-preview')).toBe(preview);
  });

  it('survives a reload through localStorage', () => {
    // 51's acceptance row: theme and split position survive a reload. The store rehydrates from
    // what it wrote, which is what a reload does.
    act(() => useUiStore.getState().setTheme({ kind: 'builtin', name: 'dark' }));
    const saved = localStorage.getItem('fluidscript.ui');
    expect(saved).toContain('"dark"');

    useUiStore.setState({ theme: { kind: 'system' } }); // the store forgets, as a reload forgets
    localStorage.setItem('fluidscript.ui', saved!); // but the browser kept what was written
    act(() => {
      void useUiStore.persist.rehydrate();
    });

    expect(useUiStore.getState().theme).toEqual({ kind: 'builtin', name: 'dark' });
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('follows the system when nothing is chosen', () => {
    expect(document.documentElement.dataset['theme']).toBeUndefined();
  });
});

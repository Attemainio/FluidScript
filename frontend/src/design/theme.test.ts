// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';

import { builtinThemes } from './builtin.ts';
import { applyTheme, contrastFailures, parseTheme, resolveTheme } from './theme.ts';
import { colorTokens } from './tokens.ts';

describe('a custom theme file', () => {
  it('that is malformed JSON is refused with the parse error and nothing changes', () => {
    // 55 error cases: refuse; keep the current theme; show the parse error.
    const root = document.documentElement;
    applyTheme(root, { name: 'dark' });

    const result = parseTheme('{ "name": "mine", "colors": { "--surface-base": "#123456" ');

    expect(result.ok).toBe(false);
    expect(result.ok ? '' : result.error).not.toBe('');
    expect(root.dataset['theme']).toBe('dark');
  });

  it('that is JSON but not a theme is refused in words', () => {
    expect(parseTheme('[1, 2]')).toMatchObject({ ok: false });
    expect(parseTheme('{ "name": "x" }')).toMatchObject({ ok: false });
    expect(parseTheme('{ "colors": { "--surface-base": 3 } }')).toMatchObject({
      ok: true,
      theme: { colors: {} },
    });
  });

  it('missing tokens falls back to the built-in value per token, and names them', () => {
    const parsed = parseTheme(
      '{ "name": "mine", "colors": { "--surface-base": "#123456", "--nonsense": "#000" } }',
    );
    expect(parsed.ok).toBe(true);
    if (!parsed.ok) {
      return;
    }

    const resolved = resolveTheme(parsed.theme, builtinThemes.light);

    expect(resolved.colors['--surface-base']).toBe('#123456');
    expect(resolved.colors['--text-primary']).toBe(builtinThemes.light['--text-primary']);
    expect(resolved.missing).toHaveLength(colorTokens.length - 1);
    expect(resolved.missing).not.toContain('--surface-base');
    expect(resolved.unknown).toEqual(['--nonsense']);
  });

  it('with a value that is not a colour treats it as missing', () => {
    const resolved = resolveTheme(
      { name: 'mine', colors: { '--text-primary': 'reddish' } },
      builtinThemes.light,
    );

    expect(resolved.invalid).toEqual(['--text-primary']);
    expect(resolved.colors['--text-primary']).toBe(builtinThemes.light['--text-primary']);
  });

  it('that fails contrast is loaded, and the failing pairs are named for the warning', () => {
    const resolved = resolveTheme(
      { name: 'faint', colors: { '--text-primary': '#F0F0F0' } },
      builtinThemes.light,
    );
    const failures = contrastFailures(resolved.colors, 'light');

    expect(failures.length).toBeGreaterThan(0);
    expect(failures.every((f) => f.text === '--text-primary')).toBe(true);
  });
});

describe('applying a theme', () => {
  it('sets the attribute for a built-in and clears any inline values', () => {
    const root = document.documentElement;
    applyTheme(root, { name: 'custom', colors: builtinThemes.dark });
    expect(root.style.getPropertyValue('--surface-base')).toBe(
      builtinThemes.dark['--surface-base'],
    );

    applyTheme(root, { name: 'light' });

    expect(root.dataset['theme']).toBe('light');
    expect(root.style.getPropertyValue('--surface-base')).toBe('');
  });

  it('sets every token inline for a custom theme under data-theme=custom', () => {
    const root = document.documentElement;
    applyTheme(root, { name: 'mine', colors: builtinThemes.dark });

    expect(root.dataset['theme']).toBe('custom');
    for (const token of colorTokens) {
      expect(root.style.getPropertyValue(token)).toBe(builtinThemes.dark[token]);
    }
  });
});

describe('following the system', () => {
  it('clears the attribute so prefers-color-scheme decides', () => {
    const root = document.documentElement;
    applyTheme(root, { name: 'dark' });
    applyTheme(root, { name: null });

    expect(root.dataset['theme']).toBeUndefined();
  });
});

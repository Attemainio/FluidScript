import { useRef, useState } from 'react';

import { builtinThemes } from '../../design/builtin.ts';
import { Button } from '../../design/primitives/Button.tsx';
import { contrastFailures, parseTheme, resolveTheme } from '../../design/theme.ts';
import { useUiStore } from '../../state/uiStore.ts';

/**
 * The toolbar's theme control (`51` app shell): system, light, dark, or a custom file. Loading a
 * file follows `55`'s error table -- malformed is refused with the parse error and the current
 * theme stays; missing tokens fall back with one warning naming them; failing contrast loads with a
 * dismissible warning.
 */
export function ThemeSwitch(): React.ReactNode {
  const theme = useUiStore((state) => state.theme);
  const setTheme = useUiStore((state) => state.setTheme);
  const [message, setMessage] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const current =
    theme.kind === 'system' ? 'system' : theme.kind === 'builtin' ? theme.name : 'custom';

  const onFile = async (event: React.ChangeEvent<HTMLInputElement>): Promise<void> => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file === undefined) {
      return;
    }

    const parsed = parseTheme(await file.text());
    if (!parsed.ok) {
      setMessage(`${file.name} was not loaded: ${parsed.error}`);
      return;
    }

    const base = current === 'dark' ? 'dark' : 'light';
    const resolved = resolveTheme(parsed.theme, builtinThemes[base]);
    setTheme({ kind: 'custom', name: resolved.name, colors: resolved.colors });

    const notes: string[] = [];
    if (resolved.missing.length > 0) {
      notes.push(
        `${resolved.missing.length} tokens missing, using ${base}: ${resolved.missing.join(', ')}`,
      );
    }
    if (resolved.invalid.length > 0) {
      notes.push(`not colours, using ${base}: ${resolved.invalid.join(', ')}`);
    }
    const failures = contrastFailures(resolved.colors, base);
    if (failures.length > 0) {
      notes.push(
        `${failures.length} text/surface pairs are under WCAG AA, first ${failures[0]?.text} on ${failures[0]?.surface}`,
      );
    }
    setMessage(notes.length === 0 ? null : `${resolved.name}: ${notes.join('; ')}`);
  };

  return (
    <div className="theme-switch" role="group" aria-label="Theme">
      <select
        aria-label="Theme"
        value={current}
        onChange={(event) => {
          const value = event.target.value;
          if (value === 'system') {
            setTheme({ kind: 'system' });
          } else if (value === 'light' || value === 'dark') {
            setTheme({ kind: 'builtin', name: value });
          }
        }}
      >
        <option value="system">System</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
        {current === 'custom' && (
          <option value="custom">{theme.kind === 'custom' ? theme.name : 'Custom'}</option>
        )}
      </select>
      <Button onClick={() => fileInput.current?.click()}>Load theme…</Button>
      <input
        ref={fileInput}
        type="file"
        accept="application/json,.json"
        hidden
        aria-hidden
        onChange={(event) => void onFile(event)}
      />
      {message !== null && (
        <span role="status" className="theme-switch__note">
          {message}{' '}
          <Button variant="quiet" onClick={() => setMessage(null)} aria-label="Dismiss">
            ×
          </Button>
        </span>
      )}
    </div>
  );
}

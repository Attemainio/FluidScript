import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { CasePicker } from './CasePicker.tsx';

describe('the case picker (D-182)', () => {
  it('offers every case, with the drawn one checked', () => {
    const html = renderToStaticMarkup(
      <CasePicker cases={{ names: ['winter', 'summer'], drawn: 'summer' }} onDraw={() => {}} />,
    );
    expect(html).toContain('aria-label="Case"');
    expect(html).toMatch(/aria-checked="false"[^>]*>winter</);
    expect(html).toMatch(/aria-checked="true"[^>]*>summer</);
  });

  it('is not there for a file with one case or none: there is nothing to choose', () => {
    expect(renderToStaticMarkup(<CasePicker cases={null} onDraw={() => {}} />)).toBe('');
    expect(
      renderToStaticMarkup(
        <CasePicker cases={{ names: ['only'], drawn: 'only' }} onDraw={() => {}} />,
      ),
    ).toBe('');
  });
});

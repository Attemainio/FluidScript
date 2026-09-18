import { Badge, Card, Panel, StatusDot } from '../../design/primitives/index.ts';

const swatches = ['cold', 'cool', 'neutral', 'warm', 'hot', 'air', 'steam'] as const;

/**
 * The design system on one page, so a theme can be judged before the editor and canvas exist to
 * show it: the fluid stops, the status badges, and `55`'s worked example coloured with the syntax
 * tokens. P5.4's shell replaces it.
 */
export function ThemePreview(): React.ReactNode {
  return (
    <div className="theme-preview">
      <Panel className="theme-preview__panel" aria-label="Fluid palette">
        <h2>Fluid</h2>
        <div className="theme-preview__row">
          {swatches.map((name) => (
            <Card
              key={name}
              className="theme-preview__swatch"
              style={{ borderColor: `var(--fluid-${name})` }}
            >
              <span
                className="theme-preview__chip"
                style={{ background: `var(--fluid-${name})` }}
                aria-hidden
              />
              {name}
            </Card>
          ))}
        </div>
        <h2>Status</h2>
        <div className="theme-preview__row">
          <Badge status="ok">converged</Badge>
          <Badge status="info">3 inferred</Badge>
          <Badge status="warning">FS4001</Badge>
          <Badge status="error">FS1507</Badge>
          <Badge status="stale">recomputing</Badge>
          <StatusDot status="ok" label="Solved in 14 ms" />
          <span className="fs-readout">12.4 m³/h</span>
        </div>
      </Panel>
      <Panel className="theme-preview__panel theme-preview__editor" aria-label="Syntax sample">
        <pre className="theme-preview__code">
          <span className="syn-keyword">circuit</span>{' '}
          <span className="syn-identifier">coolingLoop</span>{' '}
          <span className="syn-comment"># name</span>
          {'\n\n'}
          <span className="syn-identifier">HE1</span>{' '}
          <span className="syn-kind">heat_exchanger</span>{' '}
          <span className="syn-parameter">power</span>
          <span className="syn-operator">=</span>
          <span className="syn-number">30</span>
          <span className="syn-unit">kW</span> <span className="syn-parameter">in</span>
          <span className="syn-operator">=</span>
          <span className="syn-number">20</span> <span className="syn-parameter">out</span>
          <span className="syn-operator">=</span>
          <span className="syn-number">50</span>
          {'\n'}
          <span className="syn-identifier">PU1</span> <span className="syn-kind">pump</span>{' '}
          <span className="syn-parameter">head</span>
          <span className="syn-operator">=</span>
          <span className="syn-reference">HE1.dp</span>
          <span className="syn-operator">*</span>
          <span className="syn-number">1.2</span>
          {'\n'}
          <span className="syn-identifier">S1</span> <span className="syn-kind">sensor</span>{' '}
          <span className="syn-string">"supply"</span>
        </pre>
      </Panel>
      <div className="theme-preview__canvas" aria-label="Canvas sample">
        <svg
          viewBox="0 0 240 80"
          width="240"
          height="80"
          role="img"
          aria-label="A pump and an exchanger"
        >
          <rect
            x="20"
            y="30"
            width="40"
            height="20"
            rx="2"
            fill="none"
            stroke="var(--fluid-cold)"
            strokeWidth="2"
          />
          <circle
            cx="120"
            cy="40"
            r="12"
            fill="none"
            stroke="var(--canvas-symbol)"
            strokeWidth="2"
          />
          <rect
            x="180"
            y="30"
            width="40"
            height="20"
            rx="2"
            fill="none"
            stroke="var(--fluid-hot)"
            strokeWidth="2"
          />
          <path d="M60 40 H108 M132 40 H180" stroke="var(--canvas-route)" strokeWidth="2" />
          <circle cx="84" cy="40" r="3" fill="var(--canvas-symbol-inferred)" />
        </svg>
      </div>
    </div>
  );
}

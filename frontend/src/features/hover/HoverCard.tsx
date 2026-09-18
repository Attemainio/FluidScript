import type { Card } from './card.ts';

/**
 * The body of a hover card (`54`): the parameters with `stated` / `sized` / `default` and the
 * basis under a sized or defaulted one, the solved state, the warnings. One component, two mount
 * points: the canvas's tooltip and the editor's.
 */
export function HoverCard({ card }: { card: Card }): React.ReactNode {
  return (
    <div className={card.inferred ? 'hover-card hover-card--inferred' : 'hover-card'}>
      <div className="hover-card__title">
        <span className="fs-mono">{card.title}</span>
        <span className="hover-card__subtitle">{card.subtitle}</span>
      </div>
      {card.parameters.length > 0 ? (
        <table className="hover-card__rows">
          <tbody>
            {card.parameters.map((row) => (
              <Row key={row.label} row={row} />
            ))}
          </tbody>
        </table>
      ) : null}
      {card.state.length > 0 ? (
        <>
          <div className="hover-card__section">state</div>
          <table className="hover-card__rows">
            <tbody>
              {card.state.map((row) => (
                <Row key={row.label} row={row} />
              ))}
            </tbody>
          </table>
        </>
      ) : null}
      {card.warnings.map((warning, index) => (
        <div key={index} className={`hover-card__warning hover-card__warning--${warning.severity}`}>
          {warning.severity === 'error' ? '✕' : '▲'} {warning.message}
        </div>
      ))}
      {card.note !== null ? <div className="hover-card__note">{card.note}</div> : null}
    </div>
  );
}

function Row({ row }: { row: Card['parameters'][number] }): React.ReactNode {
  return (
    <>
      <tr>
        <td className="hover-card__label">{row.label}</td>
        <td className="hover-card__value fs-mono">
          {row.value} {row.unit}
        </td>
        <td className={`hover-card__source hover-card__source--${row.source ?? 'state'}`}>
          {row.source ?? ''}
        </td>
      </tr>
      {row.basis !== undefined ? (
        <tr>
          <td />
          <td colSpan={2} className="hover-card__basis">
            {row.basis}
          </td>
        </tr>
      ) : null}
    </>
  );
}

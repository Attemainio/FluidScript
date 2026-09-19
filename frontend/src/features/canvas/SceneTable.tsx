import type { Diagnostic, ModelContract } from '../../api/types.ts';
import { componentCard, connectionCard, type CardRow } from '../hover/card.ts';

/**
 * The diagram as a table (`53`, `R-42`): every component and every connection with the same
 * state, provenance and diagnostics the hover card shows, built from the same card so the two
 * cannot disagree (`62`). A screen reader reads it in layout order; a keyboard user opens it with
 * the disclosure. It is rebuilt from the model on every render, which is what "synchronized" means
 * here: there is no second copy of the state to fall behind.
 */
export function SceneTable({
  model,
  diagnostics,
  order,
}: {
  readonly model: ModelContract;
  readonly diagnostics: readonly Diagnostic[];
  /** Component ids in the drawing's order, so the table reads as the diagram does. */
  readonly order: readonly string[];
}): React.ReactNode {
  const ids = [...order, ...model.components.map((c) => c.id).filter((id) => !order.includes(id))];
  const rows = ids.flatMap((id) => {
    const card = componentCard(model, id, diagnostics);
    const component = model.components.find((c) => c.id === id);
    return card === null || component === undefined ? [] : [{ id, card, component }];
  });
  const connections = model.connections.flatMap((connection) => {
    const card = connectionCard(model, connection.id);
    return card === null ? [] : [{ id: connection.id, card }];
  });

  return (
    <details className="scene-table">
      <summary>Diagram as a table</summary>
      <table>
        <caption>
          Components, in drawing order: what the script stated, what was sized or defaulted, and the
          solved state at each
        </caption>
        <thead>
          <tr>
            <th scope="col">Component</th>
            <th scope="col">Kind</th>
            <th scope="col">Parameters</th>
            <th scope="col">State</th>
            <th scope="col">Diagnostics</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ id, card, component }) => (
            <tr key={id} data-id={id}>
              <th scope="row">
                {card.title}
                {component.tag !== null ? <span className="scene-table__id"> ({id})</span> : null}
              </th>
              <td>
                {component.kind}
                {card.inferred ? ', inferred' : ''}
              </td>
              <td>{card.parameters.length === 0 ? '—' : <Rows rows={card.parameters} />}</td>
              <td>{card.state.length === 0 ? 'not solved' : <Rows rows={card.state} />}</td>
              <td>
                {card.warnings.length === 0 ? (
                  'none'
                ) : (
                  <ul>
                    {card.warnings.map((warning, index) => (
                      <li key={index}>
                        {warning.severity}: {warning.message}
                      </li>
                    ))}
                  </ul>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <table>
        <caption>Connections: each line of the drawing and the flow it carries</caption>
        <thead>
          <tr>
            <th scope="col">Connection</th>
            <th scope="col">Pipe</th>
            <th scope="col">State</th>
          </tr>
        </thead>
        <tbody>
          {connections.map(({ id, card }) => (
            <tr key={id} data-id={id}>
              <th scope="row">{card.title}</th>
              <td>{card.subtitle}</td>
              <td>{card.state.length === 0 ? 'not solved' : <Rows rows={card.state} />}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}

function Rows({ rows }: { readonly rows: readonly CardRow[] }): React.ReactNode {
  return (
    <ul>
      {rows.map((row) => (
        <li key={row.label}>
          {row.label} {row.value}
          {row.unit.length > 0 ? ` ${row.unit}` : ''}
          {row.source !== undefined ? ` (${row.source})` : ''}
          {row.basis !== undefined ? `: ${row.basis}` : ''}
        </li>
      ))}
    </ul>
  );
}

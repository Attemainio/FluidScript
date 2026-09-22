import type { Diagnostic, Metadata, ModelContract, Quantity } from '../../api/types.ts';

/** One row of a hover card: a name, a value with its unit, and for a parameter where it came from. */
export interface CardRow {
  readonly label: string;
  readonly value: string;
  readonly unit: string;
  /** `stated`, `sized` or `default` for a parameter (`26`); absent for a state row. */
  readonly source?: string;
  /** Why a sized or defaulted value is what it is; the line under the row. */
  readonly basis?: string;
}

/** The hover card (`54` Hover): the resolved state of one thing, from the model in hand. */
export interface Card {
  readonly title: string;
  readonly subtitle: string;
  readonly inferred: boolean;
  readonly parameters: readonly CardRow[];
  readonly state: readonly CardRow[];
  readonly warnings: readonly {
    readonly severity: 'error' | 'warning';
    readonly message: string;
  }[];
  /** A note under the card: for an inferred component, what it is and what will be possible (`54`). */
  readonly note: string | null;
}

/** Formats a number for a readout: up to four significant digits, no trailing zeros, thousands unbroken. */
export function formatValue(value: number | null): string {
  if (value === null) {
    return '—';
  }
  if (!Number.isFinite(value)) {
    return String(value);
  }
  if (value === 0) {
    return '0';
  }
  const magnitude = Math.floor(Math.log10(Math.abs(value)));
  const decimals = Math.max(0, 3 - magnitude);
  return Number(value.toFixed(Math.min(decimals, 6))).toString();
}

const stateLabels: Readonly<Record<string, string>> = {
  flow: 'flow',
  tIn: 'in',
  tOut: 'out',
  pIn: 'p in',
  pOut: 'p out',
  dp: 'Δp',
  velocity: 'velocity',
  re: 'Re',
  power: 'power',
  head: 'head',
  t: 't',
  p: 'p',
};

/** The card of a component, or `null` when the model has no such component. */
export function componentCard(
  model: ModelContract | null,
  id: string,
  diagnostics: readonly Diagnostic[],
): Card | null {
  const component = model?.components.find((c) => c.id === id);
  if (component === undefined) {
    return null;
  }
  const inferred = component.origin.startsWith('inferred');

  const parameters: CardRow[] = Object.entries(component.parameters).flatMap(([name, p]) =>
    p === undefined
      ? []
      : [
          {
            label: name,
            value: formatValue(p.value),
            unit: p.unit ?? '',
            source: p.source,
            ...(p.basis !== null && p.basis !== undefined && p.source !== 'stated'
              ? { basis: p.basis }
              : {}),
          },
        ],
  );

  const state: CardRow[] = [];
  if (component.state !== null) {
    for (const [key, label] of Object.entries(stateLabels)) {
      const quantity = (component.state as Record<string, Quantity | null | undefined>)[key];
      if (quantity !== null && quantity !== undefined && quantity.value !== null) {
        state.push({ label, value: formatValue(quantity.value), unit: quantity.unit });
      }
    }
    for (const [name, quantity] of Object.entries(component.state.solved ?? {})) {
      if (quantity !== null && quantity !== undefined && quantity.value !== null) {
        state.push({
          label: `${name} (solved)`,
          value: formatValue(quantity.value),
          unit: quantity.unit,
        });
      }
    }
  }

  const warnings = diagnostics
    .filter((d) => d.component === id && (d.severity === 'error' || d.severity === 'warning'))
    .map((d) => ({ severity: d.severity as 'error' | 'warning', message: d.message }));

  return {
    title: component.tag ?? component.id,
    subtitle: component.tag === null ? component.kind : `${component.id} · ${component.kind}`,
    inferred,
    parameters,
    state,
    warnings,
    note: inferred
      ? 'Added by the compiler; writing it into the script arrives with the editing commands.'
      : null,
  };
}

/** The card of a connection: its flow, and the pipe behind it where the line carries one. */
export function connectionCard(model: ModelContract | null, id: string): Card | null {
  const connection = model?.connections.find((c) => c.id === id);
  if (connection === undefined || model === null) {
    return null;
  }
  const state: CardRow[] = [];
  if (connection.state?.flow !== undefined && connection.state.flow.value !== null) {
    state.push({
      label: 'flow',
      value: formatValue(connection.state.flow.value),
      unit: connection.state.flow.unit,
    });
  }
  const ends = [connection.from.component, connection.to.component];
  const pipe = model.components.find((c) => c.kind === 'pipe' && ends.includes(c.id));
  const parameters: CardRow[] = [];
  if (pipe !== undefined) {
    for (const [name, p] of Object.entries(pipe.parameters)) {
      if (p !== undefined && p.value !== null) {
        parameters.push({
          label: name,
          value: formatValue(p.value),
          unit: p.unit ?? '',
          source: p.source,
        });
      }
    }
    // 54: flow, velocity, Reynolds number and pressure drop, the numbers a designer wants about a run (A-6).
    for (const [key, label] of [
      ['velocity', 'velocity'],
      ['re', 'Re'],
      ['dp', 'Δp'],
    ] as const) {
      const quantity = pipe.state?.[key];
      if (quantity !== null && quantity !== undefined && quantity.value !== null) {
        state.push({ label, value: formatValue(quantity.value), unit: quantity.unit });
      }
    }
  }
  const from =
    connection.from.port === null
      ? connection.from.component
      : `${connection.from.component}.${connection.from.port}`;
  const to =
    connection.to.port === null
      ? connection.to.component
      : `${connection.to.component}.${connection.to.port}`;
  return {
    title: `${from} – ${to}`,
    subtitle: pipe === undefined ? 'connection' : `pipe ${pipe.id}`,
    inferred: false,
    parameters,
    state,
    warnings: [],
    note: null,
  };
}

/** A `let`'s card: its value and dimension, or that it waits for the solve. */
export function bindingCard(model: ModelContract | null, name: string): Card | null {
  const binding = model?.bindings.find((b) => b.name === name);
  if (binding === undefined) {
    return null;
  }
  const dimension =
    binding.dimension ?? (binding.siUnit === null ? 'dimensionless' : binding.siUnit);
  return {
    title: name,
    subtitle: binding.value === null ? 'let · deferred to the solve' : `let · ${dimension}`,
    inferred: false,
    parameters: [],
    state:
      binding.value === null
        ? []
        : [{ label: 'value', value: formatValue(binding.value), unit: binding.unit ?? '' }],
    warnings: [],
    note: null,
  };
}

/**
 * The card of a quantity literal (`52`): its value in SI and in every other unit of its dimension,
 * converted from the factors the metadata carries (A-6), so the client holds no unit table of its own.
 */
export function quantityCard(text: string, metadata: Metadata): Card | null {
  const symbol = text.replace(/^[\d.eE+-]+\s*/, '');
  const written = Number(text.slice(0, text.length - symbol.length).trim());
  const dimension = metadata.dimensions.find((d) => d.units.includes(symbol));
  if (dimension === undefined || Number.isNaN(written)) {
    return null;
  }
  const from = dimension.conversions.find((c) => c.symbol === symbol);
  const state: CardRow[] = [];
  if (from !== undefined) {
    const si = written * from.factor + from.offset;
    state.push({ label: 'SI', value: formatValue(si), unit: dimension.siUnit });
    for (const to of dimension.conversions) {
      if (to.symbol !== symbol && to.symbol !== dimension.siUnit) {
        state.push({ label: to.symbol, value: formatValue((si - to.offset) / to.factor), unit: to.symbol });
      }
    }
  }
  return {
    title: text,
    subtitle: `${dimension.name} · canonical ${dimension.canonicalUnit ?? dimension.siUnit}`,
    inferred: false,
    parameters: [],
    state,
    warnings: [],
    note: null,
  };
}

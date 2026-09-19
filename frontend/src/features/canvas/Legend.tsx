import type { Scale } from '../../api/types.ts';
import { formatValue } from '../hover/card.ts';
import { bandsOf, legendNote, legendTitle, ticksOf } from './legend.ts';

/**
 * The colour scale's legend (`57`), bottom-right of the canvas: the property and its unit, the
 * ramp with its ticks, the switcher for the other properties the script offers, and the hover
 * that lights up every element in a band. A scale is never drawn without it (invariant 1).
 */
export function Legend({
  scale,
  available,
  scales,
  active,
  onSwitch,
  onBand,
}: {
  scale: Scale;
  available: readonly string[];
  scales: Readonly<Record<string, Scale | undefined>>;
  active: string;
  onSwitch: (property: string) => void;
  onBand: (band: readonly [number, number] | null) => void;
}): React.ReactNode {
  const note = legendNote(scale);
  const ticks = scale.domain === null ? [] : ticksOf(scale.domain.min, scale.domain.max);
  const bands = bandsOf(ticks);
  const ramp = `linear-gradient(in oklab to right, var(--fluid-cold), var(--fluid-cool), var(--fluid-neutral), var(--fluid-warm), var(--fluid-hot))`;

  return (
    <div className="legend" aria-label="Colour scale">
      <div className="legend__title">{legendTitle(scale)}</div>
      {note === null ? (
        <div className="legend__scale" onPointerLeave={() => onBand(null)}>
          <div className="legend__ramp" style={{ background: ramp }}>
            {bands.map((band) => (
              <div
                key={band[0]}
                className="legend__band"
                style={{ left: `${band[0] * 100}%`, width: `${(band[1] - band[0]) * 100}%` }}
                onPointerEnter={() => onBand(band)}
                title={`${formatValue(valueAt(scale, band[0]))} to ${formatValue(valueAt(scale, band[1]))}`}
              />
            ))}
          </div>
          <div className="legend__ticks">
            {ticks.map((tick) => (
              <span
                key={tick.value}
                className="legend__tick"
                style={{ left: `${tick.position * 100}%` }}
              >
                {formatValue(tick.value)}
              </span>
            ))}
          </div>
        </div>
      ) : (
        <div className="legend__note">{note}</div>
      )}
      {available.length > 1 ? (
        <div className="legend__switcher" role="radiogroup" aria-label="Property">
          {available.map((property) => (
            <button
              key={property}
              type="button"
              role="radio"
              aria-checked={property === active}
              className={
                property === active ? 'legend__choice legend__choice--active' : 'legend__choice'
              }
              onClick={() => onSwitch(property)}
            >
              {(scales[property]?.displayName ?? property).toLowerCase()}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function valueAt(scale: Scale, position: number): number {
  const domain = scale.domain!;
  return domain.min + position * (domain.max - domain.min);
}

import type { Cases } from '../../api/types.ts';

/**
 * Which case the canvas draws, for a file that declares two or more (`D-182`). The plant is the merged one in every
 * case (`D-143`): choosing a case asks the host for that case's state on it, and changes no size. The script is not
 * edited; the choice is the interface's, like the property the colours follow.
 */
export function CasePicker({
  cases,
  onDraw,
}: {
  cases: Cases | null | undefined;
  onDraw: (name: string) => void;
}): React.ReactNode {
  if (cases === null || cases === undefined || cases.names.length < 2) {
    return null;
  }

  return (
    <div className="case-picker" role="radiogroup" aria-label="Case">
      <span className="case-picker__title">Case</span>
      {cases.names.map((name) => (
        <button
          key={name}
          type="button"
          role="radio"
          aria-checked={name === cases.drawn}
          className={
            name === cases.drawn ? 'legend__choice legend__choice--active' : 'legend__choice'
          }
          onClick={() => {
            if (name !== cases.drawn) {
              onDraw(name);
            }
          }}
        >
          {name}
        </button>
      ))}
    </div>
  );
}

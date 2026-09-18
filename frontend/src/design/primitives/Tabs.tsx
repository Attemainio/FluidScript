import { join } from '../classNames.ts';

/** One tab: its id, its label, and whether it carries unsaved work (`D-39`'s dot). */
export interface Tab {
  readonly id: string;
  readonly label: string;
  readonly dirty?: boolean;
}

/**
 * A tab strip. Selection is the caller's state; the strip only reports it. Arrow keys move between
 * tabs, as the ARIA tabs pattern says.
 */
export function Tabs({
  tabs,
  selected,
  onSelect,
  className,
}: {
  readonly tabs: readonly Tab[];
  readonly selected: string;
  readonly onSelect: (id: string) => void;
  readonly className?: string;
}): React.ReactNode {
  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>): void => {
    const index = tabs.findIndex((tab) => tab.id === selected);
    if (index < 0) {
      return;
    }
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0) {
      return;
    }
    event.preventDefault();
    const next = tabs[(index + step + tabs.length) % tabs.length];
    if (next !== undefined) {
      onSelect(next.id);
    }
  };

  return (
    <div role="tablist" className={join('fs-tabs', className)} onKeyDown={onKeyDown}>
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={tab.id === selected}
          tabIndex={tab.id === selected ? 0 : -1}
          className={join('fs-tab', tab.id === selected && 'fs-tab--selected')}
          onClick={() => onSelect(tab.id)}
        >
          {tab.label}
          {tab.dirty === true && <span className="fs-tab__dirty" aria-label="unsaved" />}
        </button>
      ))}
    </div>
  );
}

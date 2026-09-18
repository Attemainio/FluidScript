import { useEffect, useId, useRef, useState } from 'react';

import { join } from '../classNames.ts';

/** One menu item: its label, what it does, an optional shortcut hint, and whether it is available now. */
export interface MenuItem {
  readonly id: string;
  readonly label: string;
  readonly onSelect: () => void;
  readonly shortcut?: string;
  readonly disabled?: boolean;
  /** Why it is disabled, as the item's title. */
  readonly reason?: string;
}

/**
 * A menu button (the ARIA menu-button pattern): the button opens a list, arrow keys move through
 * it, Enter or Space picks, Escape and a click outside close it and return focus to the button.
 */
export function Menu({
  label,
  items,
  className,
}: {
  readonly label: string;
  readonly items: readonly MenuItem[];
  readonly className?: string;
}): React.ReactNode {
  const [open, setOpen] = useState(false);
  const [focused, setFocused] = useState(0);
  const button = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLDivElement>(null);
  const id = useId();

  useEffect(() => {
    if (!open) {
      return;
    }
    const item = list.current?.querySelectorAll<HTMLElement>('[role="menuitem"]')[focused];
    item?.focus();
  }, [open, focused]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const onPointerDown = (event: PointerEvent): void => {
      const target = event.target as Node;
      if (!list.current?.contains(target) && !button.current?.contains(target)) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  const close = (): void => {
    setOpen(false);
    button.current?.focus();
  };

  const onKeyDown = (event: React.KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      close();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      setFocused((i) => (i + 1) % items.length);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setFocused((i) => (i - 1 + items.length) % items.length);
    } else if (event.key === 'Home') {
      setFocused(0);
    } else if (event.key === 'End') {
      setFocused(items.length - 1);
    }
  };

  return (
    <div className={join('fs-menu', className)}>
      <button
        ref={button}
        type="button"
        className="fs-button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => {
          setFocused(0);
          setOpen((o) => !o);
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            setFocused(0);
            setOpen(true);
          }
        }}
      >
        {label} ▾
      </button>
      {open ? (
        <div
          ref={list}
          id={id}
          role="menu"
          aria-label={label}
          className="fs-menu__list"
          onKeyDown={onKeyDown}
        >
          {items.map((item) => (
            <button
              key={item.id}
              type="button"
              role="menuitem"
              className="fs-menu__item"
              tabIndex={-1}
              aria-disabled={item.disabled === true}
              title={item.disabled === true ? item.reason : undefined}
              onClick={() => {
                if (item.disabled === true) {
                  return;
                }
                close();
                item.onSelect();
              }}
            >
              <span>{item.label}</span>
              {item.shortcut !== undefined ? (
                <kbd className="fs-menu__shortcut">{item.shortcut}</kbd>
              ) : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

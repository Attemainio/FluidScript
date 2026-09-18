import { useEffect, useId, useRef } from 'react';

/**
 * A modal question. Focus moves into it on open and back to where it was on close; Tab cycles
 * inside it; Escape is the cancel answer. Built on a div rather than `<dialog>` so that the same
 * code runs where `showModal` is missing (jsdom), with the ARIA the element would have given.
 */
export function Dialog({
  title,
  message,
  children,
  choices,
  onChoose,
}: {
  readonly title: string;
  readonly message: string;
  readonly children?: React.ReactNode;
  readonly choices: readonly {
    readonly id: string;
    readonly label: string;
    readonly primary?: boolean;
  }[];
  readonly onChoose: (id: string) => void;
}): React.ReactNode {
  const box = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const messageId = useId();

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const first =
      box.current?.querySelector<HTMLElement>('.fs-dialog__primary') ??
      box.current?.querySelector<HTMLElement>('button');
    first?.focus();
    return () => previous?.focus();
  }, []);

  const onKeyDown = (event: React.KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      onChoose('cancel');
      return;
    }
    if (event.key !== 'Tab' || box.current === null) {
      return;
    }
    const focusable = Array.from(
      box.current.querySelectorAll<HTMLElement>(
        'button, [href], textarea, input, [tabindex]:not([tabindex="-1"])',
      ),
    );
    if (focusable.length === 0) {
      return;
    }
    const first = focusable[0]!;
    const last = focusable[focusable.length - 1]!;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return (
    <div className="fs-dialog__backdrop" onKeyDown={onKeyDown}>
      <div
        ref={box}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        className="fs-dialog"
      >
        <h2 id={titleId} className="fs-dialog__title">
          {title}
        </h2>
        <p id={messageId} className="fs-dialog__message">
          {message}
        </p>
        {children}
        <div className="fs-dialog__choices">
          {choices.map((choice) => (
            <button
              key={choice.id}
              type="button"
              className={
                choice.primary === true
                  ? 'fs-button fs-button--primary fs-dialog__primary'
                  : 'fs-button'
              }
              onClick={() => onChoose(choice.id)}
            >
              {choice.label}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

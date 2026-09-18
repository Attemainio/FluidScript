import type { HTMLAttributes } from 'react';

import { join } from '../classNames.ts';

/** A status: the five `--status-*` tokens, plus `neutral` for a count that means nothing. */
export type Status = 'ok' | 'info' | 'warning' | 'error' | 'stale' | 'neutral';

/** A small label carrying a status colour, for a diagnostic count or a solver state. */
export function Badge({
  status = 'neutral',
  className,
  ...rest
}: HTMLAttributes<HTMLSpanElement> & { readonly status?: Status }): React.ReactNode {
  return <span className={join('fs-badge', `fs-badge--${status}`, className)} {...rest} />;
}

/**
 * A dot carrying a status colour beside a text that says the same thing, so the state reads without
 * colour (`51` invariant 8c). The label is required for that reason.
 */
export function StatusDot({
  status,
  label,
  className,
  ...rest
}: HTMLAttributes<HTMLSpanElement> & {
  readonly status: Status;
  readonly label: string;
}): React.ReactNode {
  return (
    <span className={join('fs-status', className)} {...rest}>
      <span className={`fs-status__dot fs-status__dot--${status}`} aria-hidden />
      {label}
    </span>
  );
}

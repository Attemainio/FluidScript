import type { HTMLAttributes } from 'react';

import { join } from '../classNames.ts';

/** A raised surface with a border: the editor pane, the canvas pane, the log. */
export function Panel({ className, ...rest }: HTMLAttributes<HTMLElement>): React.ReactNode {
  return <section className={join('fs-panel', className)} {...rest} />;
}

/** A card: a panel's smaller sibling, for a hover detail or a legend. */
export function Card({ className, ...rest }: HTMLAttributes<HTMLDivElement>): React.ReactNode {
  return <div className={join('fs-card', className)} {...rest} />;
}

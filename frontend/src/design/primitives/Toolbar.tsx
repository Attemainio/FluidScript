import type { HTMLAttributes } from 'react';

import { join } from '../classNames.ts';

/** A horizontal row of controls with the toolbar's spacing and a bottom border. */
export function Toolbar({ className, ...rest }: HTMLAttributes<HTMLDivElement>): React.ReactNode {
  return <div role="toolbar" className={join('fs-toolbar', className)} {...rest} />;
}

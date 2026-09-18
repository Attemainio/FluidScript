import type { ButtonHTMLAttributes } from 'react';

import { join } from '../classNames.ts';

/** A square button holding one icon. The label is required, because the icon is not text. */
export function IconButton({
  label,
  className,
  type = 'button',
  children,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { readonly label: string }): React.ReactNode {
  return (
    <button
      type={type}
      className={join('fs-icon-button', className)}
      aria-label={label}
      title={label}
      {...rest}
    >
      {children}
    </button>
  );
}

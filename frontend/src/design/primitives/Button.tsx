import type { ButtonHTMLAttributes } from 'react';

import { join } from '../classNames.ts';

/** A button's emphasis: the default outlined control, a filled primary, or a quiet text button. */
export type ButtonVariant = 'default' | 'primary' | 'quiet';

/** A button, tokens only. Keyboard focus draws `--focus-ring`, as every focusable primitive does. */
export function Button({
  variant = 'default',
  className,
  type = 'button',
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  readonly variant?: ButtonVariant;
}): React.ReactNode {
  return (
    <button
      type={type}
      className={join('fs-button', `fs-button--${variant}`, className)}
      {...rest}
    />
  );
}

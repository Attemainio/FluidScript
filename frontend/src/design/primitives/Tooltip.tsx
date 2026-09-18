import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';

import { join } from '../classNames.ts';

/**
 * A card pinned near a point (`55` Tooltip): placed to the point's lower right, flipped left or up
 * where it would leave its container, never covering the point itself. The container is the
 * nearest positioned ancestor; the point is in its coordinates.
 */
export function Tooltip({
  at,
  container,
  className,
  children,
}: {
  at: { x: number; y: number };
  container: { width: number; height: number };
  className?: string;
  children: ReactNode;
}): ReactNode {
  const ref = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const gap = 12;

  useLayoutEffect(() => {
    const element = ref.current;
    if (element !== null) {
      setSize({ width: element.offsetWidth, height: element.offsetHeight });
    }
  }, [children]);

  let left = at.x + gap;
  let top = at.y + gap;
  if (left + size.width > container.width) {
    left = Math.max(0, at.x - gap - size.width);
  }
  if (top + size.height > container.height) {
    top = Math.max(0, at.y - gap - size.height);
  }

  return (
    <div
      ref={ref}
      role="tooltip"
      className={join('fs-tooltip', className)}
      style={{ left, top, visibility: size.width === 0 ? 'hidden' : 'visible' }}
    >
      {children}
    </div>
  );
}

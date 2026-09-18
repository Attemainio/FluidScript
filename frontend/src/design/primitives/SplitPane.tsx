import { useCallback, useRef } from 'react';

import { join } from '../classNames.ts';

/**
 * Two panes side by side with a draggable divider. The ratio is the caller's state, so it can
 * persist (`51`: the split position survives a reload). The divider is a separator the keyboard
 * moves with the arrow keys; the pointer drags it directly, with no motion (`55`: panel resize
 * follows the pointer).
 */
export function SplitPane({
  ratio,
  onRatioChange,
  first,
  second,
  min = 0.2,
  max = 0.8,
  className,
}: {
  readonly ratio: number;
  readonly onRatioChange: (ratio: number) => void;
  readonly first: React.ReactNode;
  readonly second: React.ReactNode;
  readonly min?: number;
  readonly max?: number;
  readonly className?: string;
}): React.ReactNode {
  const container = useRef<HTMLDivElement>(null);
  const clamp = useCallback((value: number) => Math.min(max, Math.max(min, value)), [min, max]);

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>): void => {
    const element = container.current;
    if (element === null) {
      return;
    }
    event.currentTarget.setPointerCapture(event.pointerId);
    const rect = element.getBoundingClientRect();
    const move = (moveEvent: PointerEvent): void => {
      if (rect.width > 0) {
        onRatioChange(clamp((moveEvent.clientX - rect.left) / rect.width));
      }
    };
    const up = (): void => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>): void => {
    const step = event.key === 'ArrowLeft' ? -0.02 : event.key === 'ArrowRight' ? 0.02 : 0;
    if (step !== 0) {
      event.preventDefault();
      onRatioChange(clamp(ratio + step));
    }
  };

  return (
    <div
      ref={container}
      className={join('fs-split', className)}
      style={{ gridTemplateColumns: `${ratio}fr auto ${1 - ratio}fr` }}
    >
      <div className="fs-split__pane">{first}</div>
      <div
        className="fs-split__divider"
        role="separator"
        aria-orientation="vertical"
        aria-valuenow={Math.round(ratio * 100)}
        aria-valuemin={Math.round(min * 100)}
        aria-valuemax={Math.round(max * 100)}
        tabIndex={0}
        onPointerDown={onPointerDown}
        onKeyDown={onKeyDown}
      />
      <div className="fs-split__pane">{second}</div>
    </div>
  );
}

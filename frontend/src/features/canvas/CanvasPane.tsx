import { useEffect, useMemo, useRef, useState } from 'react';

/** `54`: long enough not to flicker while the cursor crosses the canvas, short enough to feel responsive. */
const hoverDelayMs = 150;

import { Tooltip } from '../../design/primitives/index.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { selectionOf, useSelectionStore } from '../../state/selectionStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { componentCard, connectionCard, type Card } from '../hover/card.ts';
import { HoverCard } from '../hover/HoverCard.tsx';
import { prepareScene } from './scene.ts';
import { detailFor } from './detail.ts';
import { Legend } from './Legend.tsx';
import { SceneView } from './SceneView.tsx';
import {
  fit,
  gridStep,
  pan,
  reset,
  rootTransform,
  visibleWorld,
  zoomAt,
  type Size,
  type Viewport,
} from './viewport.ts';

/**
 * The drawing pane (`53`): the last successful model's scene under a CAD viewport. Wheel zooms
 * about the cursor, Shift+wheel and a trackpad's horizontal scroll pan, middle or Space+drag pans,
 * `F` fits, `Home` resets. The viewport is per pane, not per document: switching tabs fits the
 * incoming scene, which is what a reader wants from a diagram they have not seen.
 */
export function CanvasPane(): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const model = useDraftStore((state) => draftOf(state, documentId).model);
  const diagnostics = useDraftStore((state) => draftOf(state, documentId).diagnostics);
  const shown = useDraftStore((state) => draftOf(state, documentId).shown);
  const setShown = useDraftStore((state) => state.setShown);
  // Stale while a compile of newer text is in flight (57 invariant 7): the colours may not be current.
  const stale = useDraftStore((state) => {
    const draft = draftOf(state, documentId);
    return draft.compiling !== null && draft.compiling > draft.modelRevision;
  });
  const [band, setBand] = useState<readonly [number, number] | null>(null);
  const selectedIds = useSelectionStore((state) => selectionOf(state, documentId));
  const select = useSelectionStore((state) => state.select);
  const clearSelection = useSelectionStore((state) => state.clear);
  const [hover, setHover] = useState<{ card: Card; at: { x: number; y: number } } | null>(null);
  const hoverTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const host = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState<Size>({ width: 0, height: 0 });
  const [view, setView] = useState<Viewport | null>(null);
  const fitted = useRef<string | null>(null);
  const drag = useRef<{ x: number; y: number } | null>(null);
  const space = useRef(false);

  const scene = useMemo(
    () => (model === null ? null : prepareScene(model, shown ?? undefined)),
    [model, shown],
  );
  const selected = useMemo(() => new Set(selectedIds), [selectedIds]);

  // The element under the pointer, if it is a symbol, a mark or a route.
  const targetOf = (
    event: React.PointerEvent | React.MouseEvent,
  ): { id: string; route: boolean } | null => {
    const element = (event.target as Element).closest('[data-id]');
    if (element === null) {
      return null;
    }
    const id = element.getAttribute('data-id') ?? '';
    return { id, route: element.classList.contains('scene__route') };
  };

  const cancelHover = (): void => {
    if (hoverTimer.current !== null) {
      clearTimeout(hoverTimer.current);
      hoverTimer.current = null;
    }
    setHover(null);
  };

  // Hover appears after 150 ms, disappears at once, and never touches the network (54).
  const onPointerOver = (event: React.PointerEvent<SVGSVGElement>): void => {
    const target = targetOf(event);
    cancelHover();
    if (target === null || drag.current !== null) {
      return;
    }
    const rect = event.currentTarget.getBoundingClientRect();
    const at = { x: event.clientX - rect.left, y: event.clientY - rect.top };
    hoverTimer.current = setTimeout(() => {
      const card = target.route
        ? connectionCard(model, target.id)
        : componentCard(model, target.id, diagnostics);
      if (card !== null) {
        setHover({ card, at });
      }
    }, hoverDelayMs);
  };

  const onClick = (event: React.MouseEvent<SVGSVGElement>): void => {
    const target = targetOf(event);
    if (target === null || target.route) {
      return;
    }
    select(documentId, target.id, 'canvas', event.shiftKey);
  };

  useEffect(() => {
    const element = host.current;
    if (element === null) {
      return;
    }
    const observe = (): void => {
      setSize({ width: element.clientWidth, height: element.clientHeight });
    };
    observe();
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(observe);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  // The first scene of a document is fitted; later models of the same document keep the viewport
  // (55: nothing animates position, and a recompile must not move the reader's view).
  useEffect(() => {
    if (scene === null || size.width === 0) {
      return;
    }
    if (fitted.current !== documentId) {
      fitted.current = documentId;
      setView(fit(scene.bounds, size));
    }
  }, [scene, size, documentId]);

  const current = view ?? reset(size);

  const onWheel = (event: React.WheelEvent<SVGSVGElement>): void => {
    event.preventDefault();
    const rect = event.currentTarget.getBoundingClientRect();
    if (event.shiftKey || (event.deltaX !== 0 && !event.ctrlKey)) {
      setView(
        pan(
          current,
          -event.deltaX - (event.shiftKey ? event.deltaY : 0),
          event.shiftKey ? 0 : -event.deltaY,
        ),
      );
      return;
    }
    const factor = Math.exp(-event.deltaY * 0.0015);
    setView(zoomAt(current, factor, { x: event.clientX - rect.left, y: event.clientY - rect.top }));
  };

  const onPointerDown = (event: React.PointerEvent<SVGSVGElement>): void => {
    if (event.button === 1 || (event.button === 0 && space.current)) {
      event.preventDefault();
      event.currentTarget.setPointerCapture(event.pointerId);
      drag.current = { x: event.clientX, y: event.clientY };
    }
  };

  const onPointerMove = (event: React.PointerEvent<SVGSVGElement>): void => {
    const start = drag.current;
    if (start === null) {
      return;
    }
    drag.current = { x: event.clientX, y: event.clientY };
    setView(pan(current, event.clientX - start.x, event.clientY - start.y));
  };

  const onPointerUp = (event: React.PointerEvent<SVGSVGElement>): void => {
    if (drag.current !== null) {
      drag.current = null;
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>): void => {
    if (event.key === ' ') {
      space.current = true;
      event.preventDefault();
    } else if (event.key === 'f' || event.key === 'F') {
      if (scene !== null) {
        setView(fit(scene.bounds, size));
      }
    } else if (event.key === 'Home') {
      // Reset view: the viewport, and the colour scale back to the script's show (57).
      setView(reset(size));
      setShown(documentId, null);
    } else if (event.key === 'Escape') {
      clearSelection(documentId);
    }
  };

  const onKeyUp = (event: React.KeyboardEvent<HTMLDivElement>): void => {
    if (event.key === ' ') {
      space.current = false;
    }
  };

  const world = visibleWorld(current, size);
  const step = gridStep(current.zoom);
  const detail = detailFor(current.zoom);

  return (
    <div
      ref={host}
      className="canvas-pane"
      aria-label="Diagram"
      tabIndex={0}
      onKeyDown={onKeyDown}
      onKeyUp={onKeyUp}
    >
      {scene === null ? <p className="canvas-pane__empty">No model yet.</p> : null}
      <svg
        className="canvas-pane__svg"
        width={size.width}
        height={size.height}
        onWheel={onWheel}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onPointerOver={onPointerOver}
        onPointerLeave={cancelHover}
        onClick={onClick}
      >
        <g transform={rootTransform(current)}>
          {step !== null ? <Grid world={world} step={step} /> : null}
          {current.zoom >= 0.5 ? <Axes /> : null}
          {scene !== null ? (
            <SceneView
              scene={scene}
              detail={detail}
              selected={selected}
              band={band}
              stale={stale}
            />
          ) : null}
        </g>
      </svg>
      {hover !== null ? (
        <Tooltip at={hover.at} container={size}>
          <HoverCard card={hover.card} />
        </Tooltip>
      ) : null}
      <div className="canvas-pane__zoom" aria-live="polite">
        {Math.round(current.zoom * 100)}%
      </div>
      {scene !== null && scene.scale !== null && model !== null ? (
        <Legend
          scale={scene.scale}
          available={scene.available}
          scales={model.visualization.scales}
          active={scene.property}
          onSwitch={(property) => setShown(documentId, property)}
          onBand={setBand}
        />
      ) : null}
    </div>
  );
}

function Grid({
  world,
  step,
}: {
  world: { x: number; y: number; width: number; height: number };
  step: number;
}): React.ReactNode {
  const lines: React.ReactNode[] = [];
  const x0 = Math.floor(world.x / step) * step;
  const y0 = Math.floor(world.y / step) * step;
  for (let x = x0; x <= world.x + world.width; x += step) {
    lines.push(<line key={`x${x}`} x1={x} y1={world.y} x2={x} y2={world.y + world.height} />);
  }
  for (let y = y0; y <= world.y + world.height; y += step) {
    lines.push(<line key={`y${y}`} x1={world.x} y1={y} x2={world.x + world.width} y2={y} />);
  }
  return (
    <g className="canvas-grid" strokeWidth={1} vectorEffect="non-scaling-stroke">
      {lines}
    </g>
  );
}

/** The origin's rays (`R-22`): X red, Y green, one world unit long with a tick at each half. */
function Axes(): React.ReactNode {
  return (
    <g className="canvas-axes" strokeWidth={1.5} fill="none">
      <g className="canvas-axes__x">
        <line x1={0} y1={0} x2={1} y2={0} vectorEffect="non-scaling-stroke" />
        <line x1={0.5} y1={-0.04} x2={0.5} y2={0.04} vectorEffect="non-scaling-stroke" />
        <line x1={1} y1={-0.06} x2={1} y2={0.06} vectorEffect="non-scaling-stroke" />
      </g>
      <g className="canvas-axes__y">
        <line x1={0} y1={0} x2={0} y2={1} vectorEffect="non-scaling-stroke" />
        <line x1={-0.04} y1={0.5} x2={0.04} y2={0.5} vectorEffect="non-scaling-stroke" />
        <line x1={-0.06} y1={1} x2={0.06} y2={1} vectorEffect="non-scaling-stroke" />
      </g>
    </g>
  );
}

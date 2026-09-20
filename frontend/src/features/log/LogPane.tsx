import { useEffect, useMemo, useRef, useState } from 'react';

import { IconButton } from '../../design/primitives/index.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { useSelectionStore } from '../../state/selectionStore.ts';
import { logHeightMax, logHeightMin, useUiStore } from '../../state/uiStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import {
  countBySeverity,
  headerStatus,
  logAsText,
  logEntries,
  visibleEntries,
  type LogEntry,
  type LogFilter,
} from './logModel.ts';

const glyphs = { error: '✕', warning: '▲', info: '·', ok: '✓' } as const;
const leaveMs = 150;

/**
 * The console log (`56`): a reconciled view of the current diagnostics, never a history. Entries
 * are keyed by code and component, so an unchanged one keeps its element and its place across
 * compiles; a resolved one lingers for the leave animation and goes. Three or more of one code
 * fold into one expandable line; the default filter hides infos and shows their count; a clean
 * solve ends in the success line; the component column selects on canvas and in the editor.
 */
export function LogPane(): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const draft = useDraftStore((state) => draftOf(state, documentId));
  const open = useUiStore((state) => state.logOpen);
  const setOpen = useUiStore((state) => state.setLogOpen);
  const filter = useUiStore((state) => state.logFilter);
  const setFilter = useUiStore((state) => state.setLogFilter);
  const height = useUiStore((state) => state.logHeight);
  const setHeight = useUiStore((state) => state.setLogHeight);
  const select = useSelectionStore((state) => state.select);
  const [text, setText] = useState('');
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const [leaving, setLeaving] = useState<readonly LogEntry[]>([]);
  const list = useRef<HTMLOListElement>(null);
  const atBottom = useRef(true);
  const previous = useRef<readonly LogEntry[]>([]);

  const entries = useMemo(
    () => logEntries(draft.diagnostics, draft.model?.solve, draft.timings),
    [draft.diagnostics, draft.model, draft.timings],
  );
  const counts = useMemo(() => countBySeverity(entries), [entries]);
  const visible = useMemo(() => visibleEntries(entries, filter, text), [entries, filter, text]);

  // A resolved entry leaves over 150 ms rather than vanishing (56 lifecycle).
  useEffect(() => {
    const keys = new Set(visible.map((e) => e.key));
    const gone = previous.current.filter((e) => !keys.has(e.key));
    previous.current = visible;
    if (gone.length === 0) {
      return;
    }
    setLeaving((current) => [...current, ...gone]);
    const timer = setTimeout(() => {
      setLeaving((current) => current.filter((e) => !gone.includes(e)));
    }, leaveMs);
    return () => clearTimeout(timer);
  }, [visible]);

  // The terminal convention: follow the bottom only while already there (56 lifecycle).
  useEffect(() => {
    const element = list.current;
    if (element !== null && atBottom.current) {
      element.scrollTop = element.scrollHeight;
    }
  }, [visible]);

  const onScroll = (): void => {
    const element = list.current;
    if (element !== null) {
      atBottom.current = element.scrollTop + element.clientHeight >= element.scrollHeight - 2;
    }
  };

  const status = headerStatus({
    compiling: draft.compiling !== null,
    offline: draft.fault?.status === 0,
    errors: counts.error,
    solve: draft.model?.solve,
    totalMs: draft.timings?.totalMs ?? null,
  });

  const toggle = (key: string): void => {
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  };

  const copy = (): void => {
    void navigator.clipboard?.writeText(logAsText(visible));
  };

  const shown = [...visible, ...leaving.filter((e) => !visible.some((v) => v.key === e.key))];

  // The top edge is a separator the pointer drags and the arrow keys move (55: panel resize follows
  // the pointer). Dragging up makes the list taller: the pane grows into the main area above it.
  const onEdgePointerDown = (event: React.PointerEvent<HTMLDivElement>): void => {
    // Optional: jsdom has no pointer capture, and the window listeners below do the work anyway.
    event.currentTarget.setPointerCapture?.(event.pointerId);
    const startY = event.clientY;
    const startHeight = height;
    const move = (moveEvent: PointerEvent): void => {
      setHeight(startHeight + (startY - moveEvent.clientY));
    };
    const up = (): void => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  };

  const onEdgeKeyDown = (event: React.KeyboardEvent<HTMLDivElement>): void => {
    const step = event.key === 'ArrowUp' ? 16 : event.key === 'ArrowDown' ? -16 : 0;
    if (step !== 0) {
      event.preventDefault();
      setHeight(height + step);
    }
  };

  return (
    <section className={open ? 'log-pane log-pane--open' : 'log-pane'} aria-label="Log">
      {open && (
        <div
          className="log-pane__edge"
          role="separator"
          aria-orientation="horizontal"
          aria-label="Resize the log"
          aria-valuenow={height}
          aria-valuemin={logHeightMin}
          aria-valuemax={logHeightMax}
          tabIndex={0}
          onPointerDown={onEdgePointerDown}
          onKeyDown={onEdgeKeyDown}
        />
      )}
      <header className="log-pane__header">
        <FilterChoice value={filter} onChange={setFilter} counts={counts} />
        <input
          className="log-pane__search"
          type="search"
          placeholder="filter"
          aria-label="Filter the log"
          value={text}
          onChange={(event) => setText(event.target.value)}
        />
        <span className="app-spacer" />
        <span className={`fs-readout log-pane__status log-pane__status--${status.severity}`}>
          {status.text}
        </span>
        <IconButton label="Copy the log as text" onClick={copy}>
          ⎘
        </IconButton>
        <IconButton
          label={open ? 'Collapse the log' : 'Expand the log'}
          onClick={() => setOpen(!open)}
        >
          {open ? '⌄' : '⌃'}
        </IconButton>
      </header>
      {open && (
        <ol className="log-pane__entries" ref={list} onScroll={onScroll} style={{ height }}>
          {shown.map((entry) => {
            const isLeaving = !visible.includes(entry);
            const isOpen = expanded.has(entry.key);
            return (
              <li
                key={entry.key}
                data-key={entry.key}
                className={`log-pane__entry log-pane__entry--${entry.severity}${isLeaving ? ' log-pane__entry--leaving' : ''}`}
              >
                <span className="log-pane__glyph" aria-label={entry.severity}>
                  {glyphs[entry.severity]}
                </span>
                {entry.component !== null ? (
                  <button
                    type="button"
                    className="log-pane__component fs-mono"
                    onClick={() => select(documentId, entry.component!, 'log')}
                  >
                    {entry.subject}
                  </button>
                ) : entry.members !== null ? (
                  <button
                    type="button"
                    className="log-pane__component fs-mono"
                    aria-expanded={isOpen}
                    onClick={() => toggle(entry.key)}
                  >
                    {entry.subject} {isOpen ? '⌃' : '⌄'}
                  </button>
                ) : (
                  <span className="log-pane__component" />
                )}
                <span className="log-pane__message" title={entry.code ?? undefined}>
                  {entry.message}
                  {entry.code !== null ? (
                    <span className="log-pane__code"> {entry.code}</span>
                  ) : null}
                </span>
                {entry.members !== null && isOpen ? (
                  <ul className="log-pane__members">
                    {entry.members.map((m, index) => (
                      <li key={`${m.component ?? ''}:${index}`}>
                        {m.component !== null ? (
                          <button
                            type="button"
                            className="log-pane__component fs-mono"
                            onClick={() => select(documentId, m.component!, 'log')}
                          >
                            {m.component}
                          </button>
                        ) : (
                          <span className="log-pane__component" />
                        )}
                        <span className="log-pane__message">{m.message}</span>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

function FilterChoice({
  value,
  onChange,
  counts,
}: {
  value: LogFilter;
  onChange: (filter: LogFilter) => void;
  counts: Record<'error' | 'warning' | 'info' | 'ok', number>;
}): React.ReactNode {
  const options: { value: LogFilter; label: string }[] = [
    { value: 'all', label: 'all' },
    { value: 'warnings', label: 'warnings' },
    { value: 'errors', label: 'errors' },
  ];
  return (
    <span className="log-pane__filters" role="radiogroup" aria-label="Show">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={value === option.value}
          className={
            value === option.value ? 'log-pane__filter log-pane__filter--on' : 'log-pane__filter'
          }
          onClick={() => onChange(option.value)}
        >
          {value === option.value ? '●' : '○'} {option.label}
        </button>
      ))}
      {counts.info > 0 && value !== 'all' ? (
        <span className="log-pane__hidden">{counts.info} info</span>
      ) : null}
    </span>
  );
}

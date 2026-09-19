import { useEffect, useId, useState } from 'react';

import { Button, Dialog } from '../../design/primitives/index.ts';
import { draftOf, useDraftStore } from '../../state/draftStore.ts';
import { useUiStore } from '../../state/uiStore.ts';
import { useWorkspaceStore } from '../../state/workspaceStore.ts';
import { prepareScene } from '../canvas/scene.ts';
import { downloadBlob } from './download.ts';
import { exportPng, type PngExportOptions } from './exportPng.ts';
import { defaultSvgOptions, exportSvg, type ExportMeta, type ExportTheme } from './exportSvg.tsx';

type Format = 'svg' | 'png';

/** The application version the build stamped in (`vite.config.ts`); a test build has none. */
const appVersion = typeof __APP_VERSION__ === 'string' ? __APP_VERSION__ : '0.0.0-dev';

/**
 * The Export button and its dialog (`59`): format, theme, what to include, and for a PNG the
 * resolution and ground. Export generates from the last model of the active document -- the same
 * prepared scene the canvas draws, with the property the reader switched to -- and offers the file
 * as a download. A refusal (a PNG too large for the browser, a symbol nothing draws) keeps the
 * dialog open with the reason, and a failed download leaves the file ready to offer again.
 * `Ctrl+E` opens it from anywhere.
 */
export function ExportButton(): React.ReactNode {
  const [open, setOpen] = useState(false);
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const hasModel = useDraftStore((state) => draftOf(state, documentId).model !== null);

  useEffect(() => {
    const onKey = (event: KeyboardEvent): void => {
      if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === 'e') {
        event.preventDefault();
        if (hasModel) {
          setOpen(true);
        }
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [hasModel]);

  return (
    <>
      <Button
        disabled={!hasModel}
        title={
          hasModel
            ? 'Export the diagram as SVG or PNG (Ctrl+E)'
            : 'Nothing to export until a model compiles'
        }
        aria-haspopup="dialog"
        onClick={() => setOpen(true)}
      >
        Export
      </Button>
      {open ? <ExportDialog onClose={() => setOpen(false)} /> : null}
    </>
  );
}

function ExportDialog({ onClose }: { readonly onClose: () => void }): React.ReactNode {
  const documentId = useWorkspaceStore((state) => state.activeDocumentId);
  const displayName = useWorkspaceStore(
    (state) => state.documents.find((d) => d.documentId === documentId)?.displayName ?? 'diagram',
  );
  const model = useDraftStore((state) => draftOf(state, documentId).model);
  const shown = useDraftStore((state) => draftOf(state, documentId).shown);
  const appTheme = useUiStore((state) => state.theme);
  const ids = useId();

  const [format, setFormat] = useState<Format>('svg');
  const [theme, setTheme] = useState<ExportTheme>(
    appTheme.kind === 'builtin' && appTheme.name === 'dark' ? 'dark' : 'light',
  );
  const [includeLegend, setIncludeLegend] = useState(defaultSvgOptions.includeLegend);
  const [includeValues, setIncludeValues] = useState(defaultSvgOptions.includeValues);
  const [includeAxes, setIncludeAxes] = useState(defaultSvgOptions.includeAxes);
  const [dpi, setDpi] = useState<PngExportOptions['dpi']>(150);
  const [transparent, setTransparent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'error' | 'warning'; text: string } | null>(null);
  const [ready, setReady] = useState<{ name: string; blob: Blob } | null>(null);

  const generate = async (): Promise<void> => {
    if (model === null) {
      setNotice({ kind: 'error', text: 'Nothing to export: the document has no model yet.' });
      return;
    }
    setBusy(true);
    setNotice(null);
    const scene = prepareScene(model, shown ?? undefined);
    const meta: ExportMeta = { documentName: displayName, generatedAt: new Date(), appVersion };
    const stem = displayName.replace(/\.fluid$/, '');
    const result =
      format === 'svg'
        ? exportSvg(model, scene, meta, { theme, includeLegend, includeValues, includeAxes })
        : await exportPng(
            model,
            scene,
            meta,
            {
              theme,
              includeLegend,
              includeValues,
              includeAxes,
              dpi,
              transparentBackground: transparent,
            },
            window,
          );
    setBusy(false);
    if (!result.ok) {
      setNotice({ kind: 'error', text: result.reason });
      return;
    }
    const file = { name: `${stem}.${format}`, blob: result.blob };
    setReady(file);
    if (result.warnings.length > 0) {
      setNotice({ kind: 'warning', text: result.warnings.join(' ') });
    }
    try {
      downloadBlob(document, file.name, file.blob);
      if (result.warnings.length === 0) {
        onClose();
      }
    } catch (error) {
      setNotice({
        kind: 'error',
        text: `The download did not start (${error instanceof Error ? error.message : String(error)}). The file is ready; try again.`,
      });
    }
  };

  const onChoose = (id: string): void => {
    if (id === 'export') {
      void generate();
    } else if (id === 'again' && ready !== null) {
      downloadBlob(document, ready.name, ready.blob);
    } else {
      onClose();
    }
  };

  return (
    <Dialog
      title="Export diagram"
      message="A standalone file of the drawing as it stands: the same symbols, pipes, colours and legend as the canvas, with the source hash and versions recorded inside."
      choices={[
        ...(ready !== null ? [{ id: 'again', label: 'Download again' }] : []),
        { id: 'cancel', label: 'Cancel' },
        { id: 'export', label: busy ? 'Exporting…' : 'Export', primary: true },
      ]}
      onChoose={onChoose}
    >
      <form className="export-form" onSubmit={(event) => event.preventDefault()}>
        <fieldset className="export-form__group">
          <legend>Format</legend>
          <label>
            <input
              type="radio"
              name={`${ids}-format`}
              checked={format === 'svg'}
              onChange={() => setFormat('svg')}
            />{' '}
            SVG, vector: opens in a browser or Inkscape, scales without loss
          </label>
          <label>
            <input
              type="radio"
              name={`${ids}-format`}
              checked={format === 'png'}
              onChange={() => setFormat('png')}
            />{' '}
            PNG, raster: pastes anywhere
          </label>
        </fieldset>
        <div className="export-form__row">
          <label htmlFor={`${ids}-theme`}>Theme</label>
          <select
            id={`${ids}-theme`}
            value={theme}
            onChange={(event) => setTheme(event.target.value === 'dark' ? 'dark' : 'light')}
          >
            <option value="light">Light, for paper and white pages</option>
            <option value="dark">Dark</option>
          </select>
        </div>
        <fieldset className="export-form__group">
          <legend>Include</legend>
          <label>
            <input
              type="checkbox"
              checked={includeLegend}
              onChange={(event) => setIncludeLegend(event.target.checked)}
            />{' '}
            Legend, the colour scale with its unit
          </label>
          <label>
            <input
              type="checkbox"
              checked={includeValues}
              onChange={(event) => setIncludeValues(event.target.checked)}
            />{' '}
            Values, the shown property under each component
          </label>
          <label>
            <input
              type="checkbox"
              checked={includeAxes}
              onChange={(event) => setIncludeAxes(event.target.checked)}
            />{' '}
            Axes, the origin&apos;s X and Y rays
          </label>
        </fieldset>
        {format === 'png' ? (
          <fieldset className="export-form__group">
            <legend>PNG</legend>
            <div className="export-form__row">
              <label htmlFor={`${ids}-dpi`}>Resolution</label>
              <select
                id={`${ids}-dpi`}
                value={dpi}
                onChange={(event) => setDpi(Number(event.target.value) as PngExportOptions['dpi'])}
              >
                <option value={96}>96 dpi, screen</option>
                <option value={150}>150 dpi, documents</option>
                <option value={300}>300 dpi, print</option>
              </select>
            </div>
            <label>
              <input
                type="checkbox"
                checked={transparent}
                onChange={(event) => setTransparent(event.target.checked)}
              />{' '}
              Transparent background
            </label>
          </fieldset>
        ) : null}
        {notice !== null ? (
          <p
            className={`export-form__notice export-form__notice--${notice.kind}`}
            role={notice.kind === 'error' ? 'alert' : 'status'}
          >
            {notice.text}
          </p>
        ) : null}
      </form>
    </Dialog>
  );
}

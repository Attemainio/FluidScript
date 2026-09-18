import { Button, SplitPane, Toolbar } from '../../design/primitives/index.ts';
import { useUiStore } from '../../state/uiStore.ts';
import { CanvasPane } from '../canvas/CanvasPane.tsx';
import { EditorPane } from '../editor/EditorPane.tsx';
import { FileDialogHost } from '../files/FileDialogHost.tsx';
import { FileMenu } from '../files/FileMenu.tsx';
import { FileNoticeBar } from '../files/FileNoticeBar.tsx';
import { LogPane } from '../log/LogPane.tsx';
import { usePipeline } from '../pipeline/pipelineContext.ts';
import { ThemeSwitch } from '../theme/ThemeSwitch.tsx';
import { DocumentTabs } from './DocumentTabs.tsx';
import { StatusLine } from './StatusLine.tsx';

/**
 * `51`'s app shell: toolbar, tabs, the editor-left canvas-right split, the log, the status line.
 * Run, Stop and Export wait for their packages and are shown disabled so the toolbar's shape is
 * the final one.
 */
export function Shell(): React.ReactNode {
  const ratio = useUiStore((state) => state.splitRatio);
  const setRatio = useUiStore((state) => state.setSplitRatio);
  const pipeline = usePipeline();

  return (
    <div className="shell">
      <Toolbar>
        <h1>FluidScript</h1>
        <FileMenu />
        <Button variant="primary" onClick={() => pipeline.flush('solve')} title="Ctrl+Shift+Enter">
          Solve
        </Button>
        <Button disabled title="Transient runs arrive with M4">
          Run
        </Button>
        <Button disabled title="Transient runs arrive with M4">
          Stop
        </Button>
        <Button disabled title="Export arrives with P5.11">
          Export
        </Button>
        <span className="app-spacer" />
        <ThemeSwitch />
      </Toolbar>
      <DocumentTabs />
      <main className="shell__main">
        <SplitPane
          ratio={ratio}
          onRatioChange={setRatio}
          first={
            <div className="editor-column">
              <FileNoticeBar />
              <EditorPane />
            </div>
          }
          second={<CanvasPane />}
        />
      </main>
      <LogPane />
      <StatusLine />
      <FileDialogHost />
    </div>
  );
}

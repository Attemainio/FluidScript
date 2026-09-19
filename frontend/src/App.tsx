import './design/themes.generated.css';
import './design/base.css';
import './design/primitives/primitives.css';
import './features/theme/theme.css';
import './features/shell/shell.css';
import './features/canvas/scene.css';

import type { FileBackend } from './files/backend.ts';
import type { RecoveryStore } from './files/recovery.ts';
import type { Clock } from './files/scheduler.ts';
import { FilesProvider } from './features/files/FilesProvider.tsx';
import type { CompilePipeline } from './features/pipeline/compilePipeline.ts';
import { PipelineProvider } from './features/pipeline/PipelineProvider.tsx';
import { Shell } from './features/shell/Shell.tsx';
import { ThemeProvider } from './features/theme/ThemeProvider.tsx';

/** The application: the theme, the pipeline, the file actions, and `51`'s shell. A test may hand in its own pipeline, file backend, recovery store and clock. */
function App({
  pipeline,
  backend,
  recovery,
  clock,
}: {
  readonly pipeline?: CompilePipeline;
  readonly backend?: FileBackend;
  readonly recovery?: RecoveryStore;
  readonly clock?: Clock;
}): React.ReactNode {
  return (
    <ThemeProvider>
      <PipelineProvider {...(pipeline === undefined ? {} : { pipeline })}>
        <FilesProvider
          {...(backend === undefined ? {} : { backend })}
          {...(recovery === undefined ? {} : { recovery })}
          {...(clock === undefined ? {} : { clock })}
        >
          <Shell />
        </FilesProvider>
      </PipelineProvider>
    </ThemeProvider>
  );
}

export default App;

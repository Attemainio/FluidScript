import './design/themes.generated.css';
import './design/base.css';
import './design/primitives/primitives.css';
import './features/theme/theme.css';
import './features/shell/shell.css';

import type { CompilePipeline } from './features/pipeline/compilePipeline.ts';
import { PipelineProvider } from './features/pipeline/PipelineProvider.tsx';
import { Shell } from './features/shell/Shell.tsx';
import { ThemeProvider } from './features/theme/ThemeProvider.tsx';

/** The application: the theme, the pipeline, and `51`'s shell. A test may hand in its own pipeline. */
function App({ pipeline }: { readonly pipeline?: CompilePipeline }): React.ReactNode {
  return (
    <ThemeProvider>
      <PipelineProvider {...(pipeline === undefined ? {} : { pipeline })}>
        <Shell />
      </PipelineProvider>
    </ThemeProvider>
  );
}

export default App;

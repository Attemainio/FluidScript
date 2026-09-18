import './design/themes.generated.css';
import './design/base.css';
import './design/primitives/primitives.css';
import './features/theme/theme.css';

import { Toolbar } from './design/primitives/index.ts';
import { ThemePreview } from './features/theme/ThemePreview.tsx';
import { ThemeProvider } from './features/theme/ThemeProvider.tsx';
import { ThemeSwitch } from './features/theme/ThemeSwitch.tsx';

/** The shell. P5.3 gives it the theme provider and a toolbar with the theme control; P5.4 the rest (`51`). */
function App(): React.ReactNode {
  return (
    <ThemeProvider>
      <Toolbar>
        <h1>FluidScript</h1>
        <span className="app-spacer" />
        <ThemeSwitch />
      </Toolbar>
      <ThemePreview />
    </ThemeProvider>
  );
}

export default App;

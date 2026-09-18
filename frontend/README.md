# FluidScript frontend

React + Vite. The plan is `plan/50-frontend/`; the shell's structure is `51`, the design system `55`.

```bash
npm ci               # install
npm run dev          # Vite on :5173, proxying /api and /ws to the host on :5080
npm test             # Vitest, the unit and component tiers (62)
npm run lint         # oxlint, zero warnings
npm run format:check # Prettier; formatting is never a review topic (04)
npm run themes       # rewrite src/design/themes.generated.css from the theme files
npm run types        # rewrite src/api/types.generated.ts from the Api's committed JSON schemas
```

`src/design/` is the design system: the token names in `tokens.ts`, the two built-in themes as
JSON under `themes/`, the stylesheet generated from them, and the primitives. No file outside it
writes a colour, size or duration; a test scans for one. `src/api/` is the typed client and the
wire types; `src/state/` the four stores; `src/features/` the shell, the editor, the canvas, the log,
the theme control and the compile pipeline (`51`).

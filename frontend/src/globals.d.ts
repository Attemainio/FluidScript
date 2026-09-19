export {};

declare global {
  /** The application version, from `package.json` at build time (vite `define`); the export's provenance names it. */
  const __APP_VERSION__: string;
}

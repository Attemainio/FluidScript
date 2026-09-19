import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

import pkg from './package.json' with { type: 'json' };

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // The export's provenance names the application version (59); nothing else reads it.
  define: { __APP_VERSION__: JSON.stringify(pkg.version) },
  server: {
    // The API host owns /api and /ws; the dev server proxies both so the frontend runs against a
    // real backend without a second origin. See plan/40-api/41-api-architecture.md.
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/ws': { target: 'ws://localhost:5080', ws: true },
    },
  },
  test: {
    // 62's frontend unit tier. jsdom only where a test touches the document; the design tests are pure.
    environment: 'node',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/test/setup.ts'],
  },
});

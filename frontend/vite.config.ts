import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
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

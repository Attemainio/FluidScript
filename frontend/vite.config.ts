import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

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
});

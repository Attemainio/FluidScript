import { defineConfig } from '@playwright/test';

// The end-to-end tier (62): a real browser against the real host. The benchmark in e2e/ is D-48's
// keystroke-to-squiggle measurement and runs on request (npm run bench), never in a gate.
export default defineConfig({
  testDir: 'e2e',
  testMatch: /.*\.(bench|e2e)\.ts$/,
  timeout: 120_000,
  retries: 0,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:5173',
    headless: true,
  },
  webServer: [
    {
      command:
        'dotnet run --project ../src/FluidScript.Api --no-launch-profile --urls http://127.0.0.1:5080',
      url: 'http://127.0.0.1:5080/api/health',
      reuseExistingServer: true,
      timeout: 180_000,
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --port 5173 --strictPort',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
});

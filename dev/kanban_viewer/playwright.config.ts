import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  use: { baseURL: 'http://127.0.0.1:5173', trace: 'retain-on-failure', hasTouch: true },
  webServer: [
    { command: 'tsx server/index.ts', url: 'http://127.0.0.1:8765/api/config', reuseExistingServer: true },
    { command: 'vite --host 127.0.0.1', url: 'http://127.0.0.1:5173', reuseExistingServer: true },
  ],
})

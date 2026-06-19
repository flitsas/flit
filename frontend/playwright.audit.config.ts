import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e-audit',
  timeout: 120000,
  expect: { timeout: 15000 },
  use: {
    baseURL: 'http://localhost:3000',
    headless: false,
    slowMo: 800,
    actionTimeout: 20000,
    navigationTimeout: 30000,
    screenshot: 'on',
    video: 'on',
  },
  workers: 1,
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});

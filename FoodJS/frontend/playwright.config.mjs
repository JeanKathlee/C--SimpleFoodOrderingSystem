import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45000,
  expect: { timeout: 10000 },
  reporter: [["list"], ["html", { open: "never" }], ["json", { outputFile: "test-results/results.json" }]],
  use: { baseURL: "http://127.0.0.1:5174", channel: "chrome", trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [{ name: "chrome-desktop", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    { command: 'powershell -NoProfile -ExecutionPolicy Bypass -File "../scripts/Start-QAApi.ps1"',
      url: "http://127.0.0.1:3004/api/health", reuseExistingServer: false, timeout: 120000 },
    { command: "npm.cmd run dev -- --host 127.0.0.1 --port 5174 --strictPort",
      url: "http://127.0.0.1:5174", reuseExistingServer: false, timeout: 30000,
      env: { CSHARP_API_URL: "http://127.0.0.1:3004", VITE_DISABLE_GOOGLE_AUTH: "true" } },
  ],
});

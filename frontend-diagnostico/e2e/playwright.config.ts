import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

/**
 * Config LOCAL de Playwright para frontend-diagnostico (el CI de FLIT no corre Playwright).
 *
 * Requiere la suite levantada con `scripts/dev/levantar-diagnostico-local.sh simulado` (hub 4022, gateway 4002,
 * core-diagnostico 4032, esta app en 4024). Se corre desde frontend/, que es quien tiene el paquete `playwright`:
 *
 *   pnpm --dir frontend exec playwright test --config ../frontend-diagnostico/e2e/playwright.config.ts --project=chromium
 *
 * Las credenciales entran solo por variables de entorno (E2E_USUARIO, E2E_CLAVE, ...; ver cabecera de cada spec).
 * Reportes, trazas y capturas van a .claude/state/ (gitignored). workers ≤ 2 por decisión del usuario (CPU).
 *
 * Sesión única por usuario (hallazgo de la corrida del 2026-11-14): el hub invalida la sesión anterior al iniciar otra con el
 * mismo usuario, así que dos tests en paralelo con E2E_USUARIO (o con el SuperAdmin) se tumban la sesión y la app queda en
 * bucle de reautenticación («Abriendo Diagnóstico…»). Por eso:
 *   - los specs de saneamiento y de vehículos declaran `test.describe.configure({ mode: "serial" })` (con
 *     `fullyParallel: false` los tests de un archivo ya comparten worker; el modo serie lo deja explícito);
 *   - al correr VARIOS archivos a la vez, usar un solo worker: `E2E_WORKERS=1` o `--workers=1` (el flag de la línea de
 *     comandos manda sobre E2E_WORKERS). El valor por defecto (2) no cambia para no frenar los specs de un solo archivo.
 */
const RAIZ = path.resolve(__dirname, "../..");
const SALIDA = process.env.E2E_SALIDA ?? path.join(RAIZ, ".claude/state/rediseno-diagnostico/evidencias/e2e-local");

export default defineConfig({
  testDir: __dirname,
  outputDir: path.join(SALIDA, "test-results"),
  timeout: 120_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: Math.min(Number(process.env.E2E_WORKERS ?? 2), 2),
  retries: 0,
  reporter: [["list"], ["html", { outputFolder: path.join(SALIDA, "html-report"), open: "never" }]],
  use: {
    baseURL: process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024",
    locale: "es-CO",
    timezoneId: "America/Bogota",
    actionTimeout: 20_000,
    navigationTimeout: 45_000,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      testIgnore: /revision-360-a11y\.spec\.ts$/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 800 } },
    },
    {
      // Revisión responsive + a11y del rediseño (HU #14562): viewport 360x800 táctil.
      name: "movil-360",
      testMatch: /revision-360-a11y\.spec\.ts$/,
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 360, height: 800 },
        deviceScaleFactor: 2,
        isMobile: true,
        hasTouch: true,
      },
    },
  ],
});

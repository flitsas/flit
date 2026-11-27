import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

/**
 * Config LOCAL de Playwright para frontend-impuestos (HU #14694 / #14695, plan §9 M21; el CI de FLIT no corre Playwright).
 * Copia la forma de frontend-diagnostico/e2e/playwright.config.ts sin importar nada de ese paquete.
 *
 * Requiere la suite levantada con `scripts/dev/levantar-diagnostico-local.sh simulado` con el bloque de Impuestos: hub 4022,
 * gateway 4002, core-api 4003, core-identity 4025, core-consultas simulado, core-impuestos 4033, el stub del API de liquidación
 * en 8898 y esta app en 4034 (docs/impuestos/guia-entorno-local.md). Se corre desde frontend/, que es quien tiene el paquete
 * `playwright`:
 *
 *   pnpm --dir frontend exec playwright test --config ../frontend-impuestos/e2e/playwright.config.ts --project=chromium --workers=1
 *
 * Las credenciales entran solo por variables de entorno (E2E_IMP_GESTOR_*, E2E_IMP_ADMIN_*, E2E_IMP_SA_*, E2E_IMP_EMPRESA; ver
 * e2e/comun.ts). Reportes, trazas y capturas van FUERA del repo (E2E_SALIDA; por defecto ../e2e-impuestos junto al repo).
 *
 * Un solo worker por defecto: el hub invalida la sesión anterior al iniciar otra con el mismo usuario (hallazgo de los E2E de
 * Diagnóstico, 2026-11-14) y el escenario por defecto del stub es global. Cada archivo declara además el modo serie.
 */
const RAIZ = path.resolve(__dirname, "../..");
const SALIDA = process.env.E2E_SALIDA ?? path.resolve(RAIZ, "..", "e2e-impuestos");

export default defineConfig({
  testDir: __dirname,
  outputDir: path.join(SALIDA, "test-results"),
  timeout: 240_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: Math.min(Number(process.env.E2E_WORKERS ?? 1), 2),
  retries: 0,
  reporter: [["list"], ["html", { outputFolder: path.join(SALIDA, "html-report"), open: "never" }]],
  use: {
    baseURL: process.env.IMPUESTOS_URL ?? "http://127.0.0.1:4034",
    locale: "es-CO",
    timezoneId: "America/Bogota",
    actionTimeout: 20_000,
    navigationTimeout: 45_000,
    acceptDownloads: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 800 } },
    },
  ],
});

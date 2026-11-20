import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

/**
 * Humo responsive de la suite en WebKit (iPhone y iPad) y escritorio. Contrato: .trees/responsive-CONTRATO.md.
 * En iOS, Safari y Chrome usan el mismo motor (WebKit): por eso los perfiles móviles corren en `webkit`.
 *
 * Corre en local contra UNA app ya levantada (el CI no corre Playwright). Desde frontend/, que trae el paquete:
 *
 *   RESP_URL=http://127.0.0.1:3000 RESP_RUTAS=/tramites,/admin/companies \
 *   RESP_LOGIN_URL=http://127.0.0.1:3000/login RESP_USUARIO=demo RESP_CLAVE=... \
 *   pnpm --dir frontend exec playwright test --config ../tests/responsive/playwright.config.ts
 *
 * Variables:
 *   RESP_URL        base de la app (obligatoria).
 *   RESP_RUTAS      rutas separadas por coma (por defecto "/").
 *   RESP_LOGIN_URL  si viene junto con RESP_USUARIO y RESP_CLAVE, el proyecto `login` entra una vez y guarda la sesión.
 *   RESP_PERFILES   filtro opcional por nombre de proyecto, p. ej. "iphone-se,ipad-mini". También sirve `--project`.
 *   RESP_SALIDA     carpeta de capturas y reporte (por defecto .claude/state/responsive, ignorada por git).
 * Un solo worker: el hub invalida la sesión anterior del mismo usuario (ver frontend-diagnostico/e2e/playwright.config.ts).
 */
const RAIZ = path.resolve(__dirname, "../..");
export const SALIDA = process.env.RESP_SALIDA ?? path.join(RAIZ, ".claude/state/responsive");
export const SESION = path.join(SALIDA, "sesion.json");
const conLogin = Boolean(process.env.RESP_LOGIN_URL && process.env.RESP_USUARIO && process.env.RESP_CLAVE);

// Anchos del contrato: iPhone 375/390/414, iPad 768/820/1024 y escritorio 1366/1920/2560.
const perfiles = [
  { name: "iphone-se", use: { ...devices["iPhone SE (3rd gen)"] } },
  { name: "iphone-14", use: { ...devices["iPhone 14"] } },
  { name: "iphone-11", use: { ...devices["iPhone 11"] } },
  { name: "ipad-mini", use: { ...devices["iPad Mini"] } },
  { name: "ipad-air", use: { ...devices["iPad (gen 7)"], viewport: { width: 820, height: 1180 } } },
  { name: "ipad-horizontal", use: { ...devices["iPad Mini landscape"], viewport: { width: 1024, height: 768 } } },
  { name: "escritorio-1366", use: { ...devices["Desktop Chrome"], viewport: { width: 1366, height: 768 } } },
  { name: "escritorio-1920", use: { ...devices["Desktop Chrome"], viewport: { width: 1920, height: 1080 } } },
  { name: "escritorio-2560", use: { ...devices["Desktop Chrome"], viewport: { width: 2560, height: 1440 } } },
];
const filtro = process.env.RESP_PERFILES?.split(",").map((p) => p.trim()).filter(Boolean);

export default defineConfig({
  testDir: __dirname,
  outputDir: path.join(SALIDA, "test-results"),
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"], ["html", { outputFolder: path.join(SALIDA, "html-report"), open: "never" }]],
  use: {
    baseURL: process.env.RESP_URL,
    locale: "es-CO",
    timezoneId: "America/Bogota",
    navigationTimeout: 45_000,
    trace: "retain-on-failure",
  },
  projects: [
    ...(conLogin ? [{ name: "login", testMatch: /login\.setup\.ts$/, use: { ...devices["Desktop Chrome"] } }] : []),
    ...perfiles
      .filter((p) => !filtro || filtro.includes(p.name))
      .map((p) => ({
        ...p,
        testMatch: /\.spec\.ts$/,
        dependencies: conLogin ? ["login"] : [],
        use: { ...p.use, ...(conLogin ? { storageState: SESION } : {}) },
      })),
  ],
});

import fs from "node:fs";
import path from "node:path";
import { expect, test, type APIRequestContext, type Locator, type Page, type Route } from "@playwright/test";

/**
 * Apoyo común de los E2E de Impuestos (HU #14694 / #14695, plan §9 M21). Copia la FORMA de los helpers de
 * frontend-diagnostico/e2e (login por el hub, sesión única por usuario, temas, axe, 360 px y teclado), sin importar nada de
 * ese paquete (EP-CF1).
 *
 * Modo de la API: la suite local real (`scripts/dev/levantar-diagnostico-local.sh simulado` con el bloque de Impuestos):
 * frontend-impuestos 4034, gateway 4002, core-impuestos 4033 y el stub del API de liquidación en 8898. Los datos se crean en
 * cada corrida desde la UI (o con `page.request`, que comparte la cookie de la sesión) con placas ficticias. Nada escribe en la
 * base. `page.route` solo se usa para variar una respuesta real DENTRO del contrato impuestos.v1.yaml 1.2.1 (estados de
 * aprovisionamiento que la suite no produce a voluntad, un 409 puntual); cada uso lo dice en el spec.
 *
 * Stub (scripts/dev/stub-impuestos, README): la placa mágica manda sobre el escenario por defecto del panel. Placas: PAZ000 paz y
 * salvo, PAR000 parcial, RAT000 rate_limited, FAL000 portal_rejected, TMO000 corta tras enviar (queda «Incierto»), REI000
 * reintentos del webhook, SCB000 sin callback, FMS000 file_missing. El escenario por defecto se cambia con
 * `POST /__panel/escenario` (tipo alta | consulta | archivo): es GLOBAL, por eso la corrida va con un solo worker y cada spec
 * que lo cambia lo restaura en afterEach.
 *
 * RUNT: en modo `simulado` el script levanta el stub del RUNT por Intempo en 8897 (scripts/dev/stub-impuestos/stub_intempo.py;
 * docs/impuestos/guia-entorno-local.md §4.1), que decide por el último carácter de la placa (o del VIN): 1 y cualquier otro,
 * incluido el 0 de las placas mágicas del stub de liquidación → encontrado, persona natural, organismo de Medellín (Antioquia),
 * y sigue al API de liquidación; 6 → organismo de Neiva (Huila), departamento inactivo («Departamento no habilitado»);
 * 7 → responde tras el timeout de Intempo (tiempo agotado, transitorio); 8 → 200 que no es JSON (respuesta inválida,
 * transitorio); 9 → 404 (no encontrado, definitivo). Las placas ficticias de aquí terminan en 1 y las mágicas en 0: las dos
 * salen encontradas en Medellín. Detalle y variables (`STUB_INTEMPO_PUERTO`, `STUB_INTEMPO_RETRASO_S`) en la guía.
 *
 * PII: solo placas ficticias y placeholders («Compañía Demo», `nombre@empresa.com`, `CC 0000000`). Nunca datos de
 * context/muestras.
 */
export const APP = process.env.IMPUESTOS_URL ?? "http://127.0.0.1:4034";
/** BFF del navegador: mismo host, con la cookie de la sesión (page.request la comparte con el contexto). */
export const API = `${APP}/api/v1/impuestos`;
export const STUB = process.env.STUB_IMPUESTOS_URL ?? "http://127.0.0.1:8898";

const RAIZ = path.resolve(__dirname, "../..");
const SALIDA = process.env.E2E_SALIDA ?? path.resolve(RAIZ, "..", "e2e-impuestos");
export const CAPTURAS = path.join(SALIDA, "capturas");
fs.mkdirSync(CAPTURAS, { recursive: true });

// ---------- Usuarios (solo por variables de entorno) ----------

export interface Credencial {
  usuario: string | undefined;
  clave: string | undefined;
}

/** gestor_impuestos: liquida, descarga y reporta; sin cerrar lotes, usuarios ni configuración. */
export const GESTOR: Credencial = { usuario: process.env.E2E_IMP_GESTOR_USUARIO, clave: process.env.E2E_IMP_GESTOR_CLAVE };
/** admin_impuestos (el AdminCompany lo recibe solo, F5b-CF2): además cierra lotes, usuarios y configuración. */
export const ADMIN: Credencial = { usuario: process.env.E2E_IMP_ADMIN_USUARIO, clave: process.env.E2E_IMP_ADMIN_CLAVE };
/** Super Admin de plataforma (tenant FLIT): backoffice y `?empresa=`. */
export const SUPER_ADMIN: Credencial = { usuario: process.env.E2E_IMP_SA_USUARIO, clave: process.env.E2E_IMP_SA_CLAVE };
/** Uuid de la compañía de GESTOR y ADMIN, con Impuestos activo y aprovisionamiento LISTO (o LISTO_SIN_WEBHOOK). */
export const EMPRESA = process.env.E2E_IMP_EMPRESA;

export function exigir(credencial: Credencial, nombre: string) {
  test.skip(!credencial.usuario || !credencial.clave, `Faltan las variables del usuario ${nombre} (E2E_IMP_*_USUARIO / _CLAVE)`);
}
export function exigirEmpresa() {
  test.skip(!EMPRESA, "Falta E2E_IMP_EMPRESA (uuid de la compañía de prueba con Impuestos)");
}

/** Login en el hub (OIDC aud=impuestos) y vuelta a la app; mismo patrón que los E2E de Diagnóstico. */
export async function entrar(page: Page, credencial: Credencial, destino = "/") {
  await page.goto(`${APP}${destino}`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(credencial.usuario ?? "");
  await page.getByLabel("Contraseña", { exact: true }).fill(credencial.clave ?? "");
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL((url) => url.href.startsWith(APP) && !url.pathname.startsWith("/api/") && !url.pathname.startsWith("/auth/"));
  await expect(page.getByText("Abriendo Impuestos…")).toHaveCount(0, { timeout: 30_000 });
}

/** `?empresa=` de una ruta (selector de compañía del Super Admin: vive en la URL). */
export const conEmpresa = (ruta: string, empresa: string | undefined | null) =>
  empresa ? `${ruta}${ruta.includes("?") ? "&" : "?"}empresa=${encodeURIComponent(empresa)}` : ruta;

export interface Sesion {
  usuario: { id: string; nombre?: string | null };
  empresa: { id: string; nombre?: string | null };
  roles: string[];
  permisos: string[];
  esSuperAdmin: boolean;
  aprovisionamiento?: { estado?: string | null; firmaConfirmada?: boolean | null } | null;
}

export async function leerSesion(page: Page): Promise<Sesion> {
  const r = await page.request.get(`${API}/sesion`);
  expect(r.status(), "GET /sesion").toBe(200);
  return (await r.json()) as Sesion;
}

// ---------- Stub del API de liquidación ----------

export type TipoEscenario = "alta" | "consulta" | "archivo";
export const ESCENARIO_POR_DEFECTO: Readonly<Record<TipoEscenario, string>> = { alta: "alta-ok", consulta: "consulta-ok-deuda", archivo: "files-ok" };

/** Cambia el escenario por defecto del stub (panel `/__panel/escenario`, formulario urlencoded; responde 303). */
export async function escenarioStub(request: APIRequestContext, tipo: TipoEscenario, valor: string) {
  const r = await request.post(`${STUB}/__panel/escenario`, { form: { tipo, valor }, maxRedirects: 0 });
  expect([200, 303], `stub: escenario ${tipo}=${valor}`).toContain(r.status());
}

export async function restaurarStub(request: APIRequestContext) {
  for (const tipo of Object.keys(ESCENARIO_POR_DEFECTO) as TipoEscenario[]) await escenarioStub(request, tipo, ESCENARIO_POR_DEFECTO[tipo]);
}

/** El stub responde (si no, el spec se salta con el motivo en vez de fallar por el entorno). */
export async function exigirStub(request: APIRequestContext) {
  const r = await request.get(`${STUB}/__estado`).catch(() => null);
  test.skip(!r || !r.ok(), `El stub de Impuestos no responde en ${STUB} (python -I scripts/dev/stub-impuestos/stub_impuestos.py)`);
}

// ---------- Datos ficticios ----------

const CONSONANTES = "BCDFGHJKLMNPRSTVWXZ";
const al = (alfabeto: string) => alfabeto[Math.floor(Math.random() * alfabeto.length)];

/**
 * Placa ficticia de 6 caracteres (T + 2 consonantes + 2 cifras + 1): con el 1 final, el stub de Intempo (8897, ver cabecera y
 * docs/impuestos/guia-entorno-local.md §4.1) la da por encontrada, persona natural, en Medellín. No choca con ninguna placa
 * mágica del stub de liquidación (todas terminan en 000).
 */
export function placaFicticia(): string {
  return `T${al(CONSONANTES)}${al(CONSONANTES)}${Math.floor(Math.random() * 10)}${Math.floor(Math.random() * 10)}1`;
}

/** `n` placas ficticias distintas de 6 caracteres (para el tope del lote; el análisis no consulta el RUNT ni el proveedor). */
export function placasUnicas(n: number): string[] {
  const vistas = new Set<string>();
  const prefijo = `T${al(CONSONANTES)}`;
  for (let i = 0; vistas.size < n; i++) {
    const letra = CONSONANTES[Math.floor(i / 1000) % CONSONANTES.length];
    vistas.add(`${prefijo}${letra}${String(i % 1000).padStart(3, "0")}`);
  }
  return [...vistas];
}

/** CSV de la carga masiva: columna «Placa» y una fila por valor (vacío = fila sin placa). */
export function csvPlacas(valores: readonly string[]): { name: string; mimeType: string; buffer: Buffer } {
  return { name: "placas-e2e.csv", mimeType: "text/csv", buffer: Buffer.from(["Placa", ...valores].join("\r\n") + "\r\n", "utf8") };
}

// ---------- page.route (variaciones dentro del contrato) ----------

/** Responde una ruta del BFF con un cuerpo JSON del contrato. */
export async function responder(page: Page, ruta: RegExp, status: number, cuerpo: unknown, metodo?: string) {
  await page.route(ruta, async (route: Route) => {
    if (metodo && route.request().method() !== metodo) return route.fallback();
    await route.fulfill({ status, contentType: "application/json", body: cuerpo === undefined ? "" : JSON.stringify(cuerpo) });
  });
}

/** Deja pasar la petición real y cambia la respuesta (dentro del contrato). */
export async function reescribir<T>(page: Page, ruta: RegExp, cambiar: (real: T) => T, metodo = "GET") {
  await page.route(ruta, async (route: Route) => {
    if (route.request().method() !== metodo) return route.fallback();
    // Si el contexto se cierra con la petición en vuelo (fin del test), Playwright lanza «Response has been disposed» o
    // «Target page, context or browser has been closed»: la petición ya no le importa a nadie y se descarta en silencio.
    try {
      const real = await route.fetch();
      const cuerpo = cambiar((await real.json()) as T);
      await route.fulfill({ response: real, body: JSON.stringify(cuerpo), headers: { ...real.headers(), "content-type": "application/json" } });
    } catch {
      await route.abort().catch(() => undefined);
    }
  });
}

/** URL de las peticiones que coinciden con `ruta` (para comprobar filtros y `?empresa=`). */
export function registrarPeticiones(page: Page, ruta: RegExp): URL[] {
  const vistas: URL[] = [];
  page.on("request", (r) => {
    if (ruta.test(r.url())) vistas.push(new URL(r.url()));
  });
  return vistas;
}

/** Ruta del BFF de Impuestos como RegExp: `rutaApi("/liquidaciones/individual")`. Admite query al final. */
const escapar = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
/** `{id}` es un segmento cualquiera: `rutaApi("/liquidaciones/{id}/cerrar")`. */
export const rutaApi = (sufijo: string) => new RegExp(`/api/v1/impuestos${sufijo.split("{id}").map(escapar).join("[^/?]+")}(\\?|$)`);

// ---------- Navegación de la app ----------

export const dock = (page: Page) => page.getByRole("navigation", { name: "Navegación principal" }).first();
/** Entrada del dock por su etiqueta (el nombre accesible del pill puede llevar más texto tras una coma). */
export const entradaDock = (page: Page, etiqueta: string) => dock(page).getByRole("link", { name: new RegExp(`^${etiqueta}\\b`) });

/** id de la liquidación y del ítem desde la URL del detalle de la placa. */
export function idsDeUrl(url: string): { liquidacionId: string; itemId: string | null } {
  const m = /\/liquidaciones\/([^/?#]+)(?:\/items\/([^/?#]+))?/.exec(new URL(url).pathname);
  if (!m) throw new Error(`La URL no es de una liquidación: ${url}`);
  return { liquidacionId: decodeURIComponent(m[1]), itemId: m[2] ? decodeURIComponent(m[2]) : null };
}

/** Liquida una placa desde /liquidaciones/nueva y espera el detalle de la placa (la app navega sola al ítem). */
export async function liquidarPlaca(page: Page, placa: string, empresa?: string | null) {
  await page.goto(`${APP}${conEmpresa("/liquidaciones/nueva", empresa)}`);
  const form = page.getByRole("form", { name: "Liquidar placa" });
  await form.getByRole("textbox", { name: "Placa" }).fill(placa);
  await form.getByRole("button", { name: "Liquidar placa" }).click();
  await page.waitForURL(/\/liquidaciones\/[^/]+\/items\/[^/?]+/);
  await expect(page.getByRole("heading", { level: 1, name: placa })).toBeVisible();
  return idsDeUrl(page.url());
}

/** Espera el chip de estado del ítem (la página se sondea sola cada 5–30 s). */
export async function esperarChip(page: Page, texto: string | RegExp, timeout = 120_000) {
  await expect(page.getByText(texto, { exact: typeof texto === "string" }).first()).toBeVisible({ timeout });
}

// ---------- Tema, axe, 360 y teclado (forma de Diagnóstico) ----------

export type Tema = "claro" | "oscuro";
export const TEMAS: readonly Tema[] = ["claro", "oscuro"];

/** Cambia el tema como el interruptor del kit: clase `dark` en <html> y preferencia `flit-theme` en localStorage. */
export async function ponerTema(page: Page, tema: Tema) {
  await page.evaluate((oscuro) => {
    localStorage.setItem("flit-theme", oscuro ? "dark" : "light");
    document.documentElement.classList.toggle("dark", oscuro);
  }, tema === "oscuro");
  await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains("dark"))).toBe(tema === "oscuro");
}

function rutaAxe(): string {
  const pnpm = path.resolve(RAIZ, "node_modules/.pnpm");
  for (const dir of fs.readdirSync(pnpm)) {
    const candidato = path.join(pnpm, dir, "node_modules/axe-core/axe.min.js");
    if (fs.existsSync(candidato)) return candidato;
  }
  throw new Error("axe-core no está en el workspace (dependencia de eslint-plugin-jsx-a11y)");
}

export interface Violacion {
  id: string;
  impacto: string;
  ayuda: string;
  nodos: string[];
}

/** Violaciones de axe (WCAG 2.x A/AA) con impacto serio o crítico. */
export async function violacionesGraves(page: Page): Promise<Violacion[]> {
  const yaInyectado = await page.evaluate(() => "axe" in window);
  if (!yaInyectado) await page.addScriptTag({ path: rutaAxe() });
  return page.evaluate(async () => {
    // @ts-expect-error axe se inyecta en la página
    const res = await window.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } });
    return (res.violations as { id: string; impact: string; help: string; nodes: { target: string[] }[] }[])
      .filter((v) => v.impact === "serious" || v.impact === "critical")
      .map((v) => ({ id: v.id, impacto: v.impact, ayuda: v.help, nodos: v.nodes.slice(0, 5).map((n) => n.target.join(" ")) }));
  });
}

/** Captura y axe en claro y en oscuro (aserción blanda: junta todas las pantallas). Deja la página en claro. */
export async function revisarTemas(page: Page, pantalla: string, captura?: string) {
  for (const tema of TEMAS) {
    await ponerTema(page, tema);
    await page.waitForTimeout(250);
    if (captura) await page.screenshot({ path: path.join(CAPTURAS, `${captura}-${pantalla}-${tema}.png`), fullPage: true });
    expect.soft(await violacionesGraves(page), `axe en ${pantalla} (${tema}): violaciones serias o críticas`).toEqual([]);
  }
  await ponerTema(page, "claro");
}

/** Vista a 360 px: la página no se desplaza a lo ancho (las tablas pasan a tarjetas o a su región desplazable). */
export async function revisar360(page: Page, pantalla: string, captura?: string) {
  const antes = page.viewportSize();
  await page.setViewportSize({ width: 360, height: 800 });
  await page.waitForTimeout(300);
  const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect.soft(desborde, `${pantalla} a 360 px: desplazamiento horizontal de la página en px`).toBeLessThanOrEqual(1);
  if (captura) await page.screenshot({ path: path.join(CAPTURAS, `${captura}-${pantalla}-360.png`), fullPage: true });
  if (antes) await page.setViewportSize(antes);
}

/** Recorre con Tab hasta que `objetivo` tiene el foco (máx. `pasos`); falla si nunca llega. */
export async function alcanzarConTab(page: Page, objetivo: Locator, pasos = 60) {
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  for (let i = 0; i < pasos; i++) {
    await page.keyboard.press("Tab");
    if (await objetivo.evaluate((el) => el === document.activeElement).catch(() => false)) return;
  }
  throw new Error(`El control no se alcanzó con Tab en ${pasos} pasos`);
}

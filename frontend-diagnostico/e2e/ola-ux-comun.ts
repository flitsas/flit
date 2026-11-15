import fs from "node:fs";
import path from "node:path";
import { expect, type Locator, type Page, type Route } from "@playwright/test";
import { API, TEMAS, ponerTema, violacionesGraves } from "./saneamiento-comun";

/**
 * Apoyo común de los E2E de la ola «modal, acciones, carga a una columna, dashboard y reportes» (2026-11-15).
 * Fuentes: .claude/state/diagnostico-ola-ux-dashboard/{spec-ui,contrato-api,particion-hus,decisiones-usuario}.md.
 *
 * Modo de la API: la suite local real (`scripts/dev/levantar-diagnostico-local.sh simulado`), como el resto de e2e/.
 * `page.route` solo se usa para forzar un estado que la suite no da sola (vacío, error, 403, 422, 429, 503, cargando) o para
 * variar una respuesta real dentro del contrato 1.5.0 (`total` por encima del máximo, capacidades de /sesion). Cada uso lo
 * dice en el spec.
 *
 * Sesión única por usuario: dos logins del mismo usuario se invalidan. Cada spec va en serie y, al correr VARIOS archivos de
 * esta ola a la vez, se usa `--workers=1` (ver cabecera de playwright.config.ts).
 *
 * Capturas: `<id>-<pantalla>-<tema>.png` en `$E2E_SALIDA/capturas` (por defecto
 * `.claude/state/diagnostico-ola-ux-dashboard/evidencias/e2e-local/capturas`). Solo placeholders (ABC123, Compañía Demo):
 * las placas de la siembra son ficticias.
 */
const SALIDA_OLA = process.env.E2E_SALIDA ?? path.resolve(__dirname, "../../.claude/state/diagnostico-ola-ux-dashboard/evidencias/e2e-local");
export const CAPTURAS_OLA = path.join(SALIDA_OLA, "capturas");
fs.mkdirSync(CAPTURAS_OLA, { recursive: true });

/** Admin de una compañía cliente (mismas variables que backoffice-llenado.spec.ts). Opcional. */
export const ADMIN = { usuario: process.env.E2E_ADMIN_USUARIO, clave: process.env.E2E_ADMIN_CLAVE };

// ---------- Rutas del BFF (contrato-api.md §0) ----------

const BASE = "\\/api\\/v1\\/diagnostico";
export const RUTA = {
  sesion: new RegExp(`${BASE}\\/sesion(\\?|$)`),
  /** E1 */
  estadisticas: new RegExp(`${BASE}\\/estadisticas\\?`),
  /** E2 */
  estadisticasGlobal: new RegExp(`${BASE}\\/backoffice\\/estadisticas\\/global\\?`),
  /** E3 */
  estadisticasEmpresa: new RegExp(`${BASE}\\/backoffice\\/estadisticas\\?`),
  /** S (selector de compañías) */
  empresas: new RegExp(`${BASE}\\/backoffice\\/empresas(\\?|$)`),
  /** R0 */
  catalogo: new RegExp(`${BASE}\\/reportes(\\?|$)`),
  /** B0 */
  catalogoBackoffice: new RegExp(`${BASE}\\/backoffice\\/reportes(\\?|$)`),
  /** R1 (no casa /excel ni /pdf: el tipo va seguido de «?») */
  reporte: new RegExp(`${BASE}\\/reportes\\/[a-z]+\\?`),
  /** B1 */
  reporteGlobal: new RegExp(`${BASE}\\/backoffice\\/reportes\\/[a-z]+\\/global\\?`),
  /** B4 */
  reporteEmpresa: new RegExp(`${BASE}\\/backoffice\\/reportes\\/[a-z]+\\?`),
  /** R2/R3, B2/B3, B5/B6 */
  exportacion: new RegExp(`${BASE}(\\/backoffice)?\\/reportes\\/[a-z]+(\\/global)?\\/(excel|pdf)(\\?|$)`),
  /** Cualquier ruta del backoffice (para comprobar que un usuario de compañía nunca la pide). */
  backoffice: new RegExp(`${BASE}\\/backoffice\\/`),
} as const;

/** Nombre del archivo de una exportación (contrato-api.md §5). Sin placa, documento, nombre ni NIT. */
export const NOMBRE_EXPORTACION = /^diagnostico-reporte-[a-z]+-\d{8}-\d{8}(-global)?\.(xlsx|pdf)$/;

// ---------- Sesión ----------

export interface Capacidades {
  crearDiagnosticos: boolean;
  verDiagnosticos: boolean;
  descargarReportes: boolean;
  derivar: boolean;
  reprocesarConsultas: boolean;
  backoffice: boolean;
  sanear?: boolean;
  verReportes?: boolean;
  verEstadisticasGlobales?: boolean;
  [otra: string]: boolean | undefined;
}
export interface Sesion {
  usuario: { id: string; esSuperAdmin: boolean };
  empresa: { id: string; nombre?: string | null };
  capacidades: Capacidades;
}

/** GET /sesion por el BFF con la cookie de la página (para saber qué debe pintar la UI). */
export async function leerSesion(page: Page): Promise<Sesion> {
  const r = await page.request.get(`${API}/sesion`);
  expect(r.ok(), `GET /sesion ${r.status()}`).toBeTruthy();
  return r.json();
}

/** ¿El usuario ve la vista global? FE-5 lo ata a `verEstadisticasGlobales`; spec-ui §4.4 a `backoffice`. */
export const tieneVistaGlobal = (s: Sesion) => s.capacidades.verEstadisticasGlobales ?? s.capacidades.backoffice;

/**
 * Varía las capacidades de la respuesta REAL de /sesion (solo lo que pinta el front; la API sigue exigiendo el permiso en
 * cada ruta). Devuelve `quitar()`.
 */
export async function variarCapacidades(page: Page, cambios: Partial<Capacidades>) {
  const manejador = async (route: Route) => {
    const real = await route.fetch();
    const cuerpo = (await real.json()) as Sesion;
    cuerpo.capacidades = { ...cuerpo.capacidades, ...cambios } as Capacidades;
    await route.fulfill({ response: real, json: cuerpo });
  };
  await page.route(RUTA.sesion, manejador);
  return { quitar: () => page.unroute(RUTA.sesion, manejador) };
}

// ---------- Fechas (hora Colombia, AAAA-MM-DD) ----------

export const hoyBogota = () => new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bogota" }).format(new Date());

export function sumarDias(fecha: string, dias: number): string {
  const d = new Date(`${fecha}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + dias);
  return d.toISOString().slice(0, 10);
}

export function diasEntre(desde: string, hasta: string): number {
  return Math.round((Date.parse(`${hasta}T00:00:00Z`) - Date.parse(`${desde}T00:00:00Z`)) / 86_400_000);
}

// ---------- Respuestas del contrato para forzar estados ----------

/** `EstadisticasDto` (contrato §1) sin datos: los 5 estados de saneamiento con ceros, serie vacía. */
export function estadisticasVacias(desde: string, hasta: string, global = false) {
  return {
    ambito: global ? { tipo: "GLOBAL", empresa_id: null, empresa_nombre: null } : { tipo: "EMPRESA", empresa_id: "00000000-0000-0000-0000-000000000000", empresa_nombre: "Compañía Demo" },
    periodo: { desde, hasta, granularidad: "DIA" },
    generado_en: `${hasta}T08:00:00-05:00`,
    resumen: {
      diagnosticos: 0, diagnosticos_express: 0, diagnosticos_masivos: 0, vehiculos: 0, vehiculos_unicos: 0,
      consultas: 0, consultas_exitosas: 0, consultas_con_error: 0, consultas_no_aplica: 0, consultas_pendientes: 0,
      tasa_exito: null, tiempo_promedio_consulta_ms: null, saneamientos_solicitados: 0, tramites_creados: 0,
    },
    serie: [],
    veredictos: { viables: 0, bloqueados: 0, sin_dictamen: 0 },
    tipos_analisis: { simple: 0, con_analisis: 0 },
    fuentes: [],
    saneamientos: ["SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE", "ANULADO"].map((estado) => ({ estado, cantidad: 0 })),
    derivaciones: { creadas: 0, omitidas: 0, fallidas: 0, en_curso: 0 },
    top_empresas: [],
  };
}

/** Responde `cuerpo` con `status` a todas las GET que cumplan `ruta` hasta `quitar()`. */
export async function responder(page: Page, ruta: RegExp, status: number, cuerpo: unknown, cabeceras: Record<string, string> = {}) {
  const manejador = async (route: Route) => {
    if (route.request().method() !== "GET") return route.fallback();
    return route.fulfill({ status, contentType: "application/json", headers: cabeceras, body: JSON.stringify(cuerpo) });
  };
  await page.route(ruta, manejador);
  return { quitar: () => page.unroute(ruta, manejador) };
}

/** Reescribe la respuesta REAL (JSON) de las GET que cumplan `ruta` con `cambiar`, hasta `quitar()`. */
export async function reescribir<T>(page: Page, ruta: RegExp, cambiar: (real: T) => T) {
  const manejador = async (route: Route) => {
    if (route.request().method() !== "GET") return route.fallback();
    const real = await route.fetch();
    await route.fulfill({ response: real, json: cambiar((await real.json()) as T) });
  };
  await page.route(ruta, manejador);
  return { quitar: () => page.unroute(ruta, manejador) };
}

/** Peticiones GET que cumplen `ruta`, desde ahora. */
export function registrarPeticiones(page: Page, ruta: RegExp): URL[] {
  const vistas: URL[] = [];
  page.on("request", (r) => {
    if (r.method() === "GET" && ruta.test(r.url())) vistas.push(new URL(r.url()));
  });
  return vistas;
}

// ---------- Modal (FE-1) ----------

/** El overlay del Modal lleva role=dialog; el panel visible es su primer hijo (packages/ui/src/Modal.tsx). */
export const panelDe = (dialogo: Locator) => dialogo.locator(":scope > div").first();

export async function anchoPanel(dialogo: Locator): Promise<number> {
  const caja = await panelDe(dialogo).boundingBox();
  expect(caja, "caja del panel del modal").toBeTruthy();
  return caja!.width;
}

/**
 * Cuenta el máximo de `[role="dialog"]` montados a la vez desde ahora (D-15: Dictamen → Deuda nunca apila dos capas).
 * Leer con `maxDialogos(page)`.
 */
export async function vigilarDialogos(page: Page) {
  await page.evaluate(() => {
    const w = window as unknown as { __maxDialogos: number; __obsDialogos?: MutationObserver };
    const contar = () => {
      w.__maxDialogos = Math.max(w.__maxDialogos ?? 0, document.querySelectorAll('[role="dialog"]').length);
    };
    w.__maxDialogos = 0;
    w.__obsDialogos?.disconnect();
    w.__obsDialogos = new MutationObserver(contar);
    w.__obsDialogos.observe(document.body, { childList: true, subtree: true });
    contar();
  });
}
export const maxDialogos = (page: Page) => page.evaluate(() => (window as unknown as { __maxDialogos: number }).__maxDialogos);

/** Clic en el velo (esquina del overlay, fuera del panel). */
export async function clicEnVelo(page: Page) {
  await page.mouse.click(4, 4);
}

// ---------- Menú «Acciones» (FE-2) ----------

/** Disparador del menú de la fila (spec-ui §2.3): «Acciones», nombre accesible «Acciones de la placa X». */
export const disparadorAcciones = (page: Page, placa: string) => page.getByRole("button", { name: `Acciones de la placa ${placa}`, exact: true });

/** Abre el menú de la fila (si ya está abierto, no lo vuelve a pulsar) y devuelve el disparador y el `role=menu`. */
export async function abrirAcciones(page: Page, placa: string) {
  const disparador = disparadorAcciones(page, placa);
  await disparador.scrollIntoViewIfNeeded();
  if ((await disparador.getAttribute("aria-expanded")) !== "true") await disparador.click();
  await expect(disparador).toHaveAttribute("aria-expanded", "true");
  const menu = page.getByRole("menu");
  await expect(menu).toBeVisible();
  return { disparador, menu };
}

/** Cierra el menú de la fila pulsando otra vez el disparador (alterna); no depende de dónde quedó el foco. */
export async function cerrarAcciones(page: Page, placa: string) {
  const disparador = disparadorAcciones(page, placa);
  if ((await disparador.getAttribute("aria-expanded")) === "true") await disparador.click();
  await expect(disparador).toHaveAttribute("aria-expanded", "false");
  await expect(page.getByRole("menu")).toHaveCount(0);
}

/** Orden de los ítems (spec-ui §2.3 con V2 adoptado en decisiones-usuario.md). */
export const ORDEN_ITEMS: readonly RegExp[] = [
  /^(Solicitar saneamiento|Quitar solicitud de saneamiento|Ver saneamiento)/,
  /^(Crear trámite|Creando…|Ver trámite #)/,
  /^Ver deuda/,
  /^Descargar PDF/,
  /^(Reintentar consultas|Reintentando…)/,
];

/** Nombres accesibles de los `menuitem` del menú abierto, en orden. */
export async function nombresItems(menu: Locator): Promise<string[]> {
  const items = await menu.getByRole("menuitem").all();
  const nombres: string[] = [];
  for (const i of items) nombres.push(((await i.textContent()) ?? "").replace(/\s+/g, " ").trim());
  return nombres;
}

/** El ítem deshabilitado con motivo (spec-ui §2.2.3): aria-disabled, sigue en el DOM y su motivo es visible y descrito. */
export async function esperarDeshabilitadoConMotivo(item: Locator, motivo?: RegExp) {
  await expect(item).toHaveAttribute("aria-disabled", "true");
  await expect(item).not.toHaveAttribute("disabled", /.*/);
  const descrito = await item.getAttribute("aria-describedby");
  expect(descrito, "aria-describedby del motivo").toBeTruthy();
  const pagina = item.page();
  const textoMotivo = pagina.locator(`[id="${descrito!.split(/\s+/)[0]}"]`);
  await expect(textoMotivo).toBeVisible();
  if (motivo) await expect(textoMotivo).toHaveText(motivo);
  await expect(item).toHaveAccessibleDescription(/\S/);
}

// ---------- Tema, captura y axe ----------

/**
 * Captura en claro y en oscuro con axe sin violaciones serias ni críticas (aserciones blandas: junta todas las pantallas).
 * Deja la página en tema claro.
 */
export async function revisarTemasOla(page: Page, pantalla: string, opciones: { captura: string; paginaCompleta?: boolean; axe?: boolean } ) {
  for (const tema of TEMAS) {
    await ponerTema(page, tema);
    await page.waitForTimeout(250);
    await page.screenshot({ path: path.join(CAPTURAS_OLA, `${opciones.captura}-${pantalla}-${tema}.png`), fullPage: opciones.paginaCompleta ?? true });
    if (opciones.axe !== false) {
      const graves = await violacionesGraves(page);
      expect.soft(graves, `axe en ${pantalla} (${tema}): violaciones serias o críticas`).toEqual([]);
    }
  }
  await ponerTema(page, "claro");
}

/** Claves de localStorage y sessionStorage (SP-18: ver y exportar un reporte no deja claves nuevas). */
export async function clavesAlmacenamiento(page: Page): Promise<string[]> {
  return page.evaluate(() => [
    ...Object.keys(localStorage).map((k) => `local:${k}`),
    ...Object.keys(sessionStorage).map((k) => `session:${k}`),
  ].sort());
}

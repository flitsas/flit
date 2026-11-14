import fs from "node:fs";
import path from "node:path";
import { expect, test, type Download, type Locator, type Page, type Route } from "@playwright/test";

/**
 * Apoyo común de los E2E de saneamiento (HU #14577 AC41, AC42 y AC45; CR-2 de la revisión de código).
 *
 * Modo de la API: la suite local real (`scripts/dev/levantar-diagnostico-local.sh simulado`), igual que el resto de
 * e2e/. Los datos se siembran en cada corrida desde la propia UI (Express y Masivo con placas ficticias de la simulación);
 * `page.route` solo se usa para inyectar un fallo puntual (red caída, 409, 422, 500), para retener una respuesta real
 * mientras se toma la captura de «cargando» o para variar una respuesta real dentro del contrato 1.4.0 (dos propietarios,
 * insignia con 3 y con 150). Cada uso lo dice en el spec.
 *
 * Simulación de core-consultas (último carácter del identificador): 1 persona natural con SIMIT en verde; 2 SIMIT en rojo con
 * 2 comparendos (en Con análisis IA el dictamen bloquea por SIMIT_VEHICULO); 3 SIMIT en amarillo con acuerdo de pago; 7 RUNT
 * con tiempo agotado (el vehículo nunca termina: NO_TERMINADO).
 *
 * Capturas: `C-n-<pantalla>-<tema>.png` en `$E2E_SALIDA/capturas` (por defecto `.claude/state/saneamiento/evidencias/e2e-local`).
 */
export const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
/** BFF del navegador: el mismo host, con la cookie de la sesión (page.request la comparte con el contexto). */
export const API = `${APP}/api/v1/diagnostico`;
const SALIDA_BASE = process.env.E2E_SALIDA ?? path.resolve(__dirname, "../../.claude/state/saneamiento/evidencias/e2e-local");
export const CAPTURAS = path.join(SALIDA_BASE, "capturas");
fs.mkdirSync(CAPTURAS, { recursive: true });

export type Tema = "claro" | "oscuro";
export const TEMAS: readonly Tema[] = ["claro", "oscuro"];

// ---------- Credenciales (solo por variables de entorno) ----------

export const CLIENTE = { usuario: process.env.E2E_USUARIO, clave: process.env.E2E_CLAVE };
export const BACKOFFICE = { usuario: process.env.E2E_BACKOFFICE_USUARIO, clave: process.env.E2E_BACKOFFICE_CLAVE };
/** Uuid de la compañía de E2E_USUARIO, para las rutas del backoffice (`?empresa=`). */
export const EMPRESA = process.env.E2E_EMPRESA;

export function exigirCliente() {
  test.skip(!CLIENTE.usuario || !CLIENTE.clave, "Faltan E2E_USUARIO / E2E_CLAVE");
}
export function exigirBackoffice() {
  test.skip(!BACKOFFICE.usuario || !BACKOFFICE.clave || !EMPRESA, "Faltan E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA");
}

/** Login en el hub (OIDC) y vuelta a la app; mismo patrón que los specs existentes. */
export async function entrar(page: Page, usuario: string, clave: string, destino = "/") {
  await page.goto(`${APP}${destino}`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel("Contraseña", { exact: true }).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL((url) => url.href.startsWith(APP) && !url.pathname.startsWith("/api/"));
}

// ---------- Datos ficticios ----------

const CONSONANTES = "BCDFGHJKLMNPRSTVWXZ";
const al = (alfabeto: string) => alfabeto[Math.floor(Math.random() * alfabeto.length)];

/**
 * Prefijo de tres letras ficticio y distinto en cada corrida («TKZ»): el filtro «Placa» de PS-3 es por prefijo, así que
 * aísla las placas de esta corrida de las de otros specs que corran a la vez.
 */
export function prefijoFicticio(): string {
  return `T${al(CONSONANTES)}${al(CONSONANTES)}`;
}

/** Placa de carro ficticia (ABC123) con el escenario de la simulación en el último dígito. */
export function placaFicticia(prefijo: string, escenario: "1" | "2" | "3" | "7"): string {
  return `${prefijo}${Math.floor(Math.random() * 10)}${Math.floor(Math.random() * 10)}${escenario}`;
}

/**
 * VIN ficticio de 17 caracteres (sin I, O ni Q) terminado en 1 (persona natural, consulta exitosa). Empieza con el prefijo
 * de la corrida para que el filtro «Placa» de PS-3, que busca por prefijo del identificador, también lo traiga.
 */
export function vinFicticio(prefijo: string): string {
  const alfabeto = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
  let vin = prefijo;
  while (vin.length < 16) vin += al(alfabeto);
  return `${vin}1`;
}

/** PDF mínimo válido: firma `%PDF-` al inicio y `%%EOF` al final (la regla del cliente y del servidor, SP-17). */
export const PDF_VALIDO = Buffer.from(
  "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n",
  "latin1",
);
/** Texto con extensión .pdf: el cliente lo rechaza antes de subirlo. */
export const PDF_FALSO = Buffer.from("Este archivo no es un PDF; solo tiene la extensión.\n", "utf8");

// ---------- Tema, axe, 360 y teclado ----------

/** Cambia el tema como el interruptor del kit: clase `dark` en <html> y preferencia `flit-theme` en localStorage. */
export async function ponerTema(page: Page, tema: Tema) {
  await page.evaluate((oscuro) => {
    localStorage.setItem("flit-theme", oscuro ? "dark" : "light");
    document.documentElement.classList.toggle("dark", oscuro);
  }, tema === "oscuro");
  await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains("dark"))).toBe(tema === "oscuro");
}

function rutaAxe(): string {
  const pnpm = path.resolve(__dirname, "../../node_modules/.pnpm");
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

/** Violaciones de axe (WCAG 2.x A/AA) con impacto serio o crítico. Las menores no hacen fallar (AC41). */
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

/**
 * Revisa la pantalla en claro y en oscuro: captura (si lleva id C-n) y axe sin violaciones serias ni críticas (AC41, AC42).
 * Las aserciones son blandas: el test sigue y junta todas las pantallas; al final falla si hubo alguna.
 * Deja la página en tema claro.
 */
export async function revisarTemas(page: Page, pantalla: string, opciones: { captura?: string; paginaCompleta?: boolean } = {}) {
  for (const tema of TEMAS) {
    await ponerTema(page, tema);
    await page.waitForTimeout(250);
    if (opciones.captura) {
      await page.screenshot({ path: path.join(CAPTURAS, `${opciones.captura}-${pantalla}-${tema}.png`), fullPage: opciones.paginaCompleta ?? true });
    }
    const graves = await violacionesGraves(page);
    expect.soft(graves, `axe en ${pantalla} (${tema}): violaciones serias o críticas`).toEqual([]);
  }
  await ponerTema(page, "claro");
}

/** C-7 / AC42: la página no se desplaza a lo ancho (las tablas pasan a tarjetas o a su propia región desplazable). */
export async function sinDesbordeHorizontal(page: Page, pantalla: string) {
  const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect.soft(desborde, `${pantalla}: desplazamiento horizontal de la página en px`).toBeLessThanOrEqual(1);
}

interface PasoTab {
  foco: string;
  invisible: boolean;
  sinIndicador: boolean;
}

/**
 * AC41 + C-9 / UA-1: recorre la pantalla con Tab y comprueba que el foco nunca cae en un control invisible (recortado a
 * 1×1 px, `sr-only`, transparente u oculto) y que cada parada tiene indicador visible (outline o sombra en el control o en
 * su contenedor inmediato). Corta al dar la vuelta o al salir de `ambito` (p. ej. el diálogo abierto).
 */
export async function recorrerConTab(page: Page, pantalla: string, opciones: { pasos?: number; ambito?: Locator } = {}) {
  const pasos = opciones.pasos ?? 40;
  if (!opciones.ambito) await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  const recorrido: PasoTab[] = [];
  let primero: string | null = null;
  for (let i = 0; i < pasos; i++) {
    await page.keyboard.press("Tab");
    const paso = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null;
      if (!el || el === document.body) return null;
      const r = el.getBoundingClientRect();
      const recortado = (n: Element) => {
        const s = getComputedStyle(n);
        return s.clip === "rect(0px, 0px, 0px, 0px)" || s.clipPath === "inset(50%)" || s.visibility === "hidden" || s.opacity === "0";
      };
      let oculto = false;
      for (let n: Element | null = el; n && n !== document.body; n = n.parentElement) if (recortado(n)) oculto = true;
      const indicador = (n: Element | null) => {
        if (!n) return false;
        const s = getComputedStyle(n);
        return (s.outlineStyle !== "none" && parseFloat(s.outlineWidth) > 0) || (s.boxShadow !== "none" && s.boxShadow !== "");
      };
      const nombre = (el.getAttribute("aria-label") || el.innerText || el.getAttribute("name") || el.tagName).trim().replace(/\s+/g, " ").slice(0, 70);
      return {
        foco: `${el.tagName.toLowerCase()}${el.getAttribute("role") ? `[${el.getAttribute("role")}]` : ""} «${nombre}»`,
        invisible: oculto || r.width < 4 || r.height < 4,
        sinIndicador: !(indicador(el) || indicador(el.parentElement) || indicador(el.parentElement?.parentElement ?? null)),
      };
    });
    if (!paso) continue;
    if (opciones.ambito && !(await opciones.ambito.evaluate((n) => n.contains(document.activeElement)))) break;
    if (paso.foco === primero) break;
    primero ??= paso.foco;
    recorrido.push(paso);
  }
  fs.writeFileSync(path.join(CAPTURAS, `tab-${pantalla}.json`), JSON.stringify(recorrido, null, 2), "utf8");
  expect.soft(recorrido.filter((p) => p.invisible).map((p) => p.foco), `${pantalla}: foco en controles invisibles`).toEqual([]);
  expect.soft(recorrido.filter((p) => p.sinIndicador).map((p) => p.foco), `${pantalla}: paradas de foco sin indicador visible`).toEqual([]);
  return recorrido;
}

/** Ancho de pantalla de la revisión: 1280 (escritorio) o 360 (móvil, AC42). */
export async function ancho(page: Page, px: 1280 | 1023 | 640 | 360) {
  await page.setViewportSize({ width: px, height: 800 });
  await page.waitForTimeout(200);
}

// ---------- Siembra de lotes desde la UI ----------

const TABLERO = /\/diagnosticos\/[0-9a-f-]{36}$/i;

/** Marca «Con Análisis IA» y el primer tipo de trámite si el contrato de la compañía lo permite. Devuelve si quedó. */
async function elegirConAnalisis(page: Page): Promise<boolean> {
  const radio = page.getByRole("radio", { name: "Con Análisis IA" });
  if (!(await radio.count()) || !(await radio.first().isEnabled())) return false;
  await radio.first().check();
  const tipo = page.getByRole("combobox", { name: /Tipo de trámite/ });
  await expect(tipo).toBeVisible();
  await tipo.selectOption({ index: 1 });
  return true;
}

/** Abre el tablero desde la pantalla de resultado («Ver tablero») o espera la redirección. Devuelve el id del lote. */
async function irAlTablero(page: Page): Promise<string> {
  const verTablero = page.getByRole("link", { name: "Ver tablero" });
  await Promise.race([verTablero.waitFor({ state: "visible" }), page.waitForURL(TABLERO)]);
  if (await verTablero.isVisible()) await verTablero.click();
  await page.waitForURL(TABLERO);
  return new URL(page.url()).pathname.split("/").at(-1)!;
}

/** Express de una placa desde /diagnosticos/nuevo (mismo flujo que e2e/express.ts, con el modo opcional). */
export async function sembrarExpress(page: Page, identificador: string, opciones: { nombre: string; conAnalisis?: boolean }) {
  await page.goto(`${APP}/diagnosticos/nuevo?modo=express&placa=${encodeURIComponent(identificador)}`);
  await expect(page.getByRole("textbox", { name: "Placa o VIN" })).toHaveValue(identificador);
  await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(opciones.nombre);
  const conAnalisis = opciones.conAnalisis ? await elegirConAnalisis(page) : false;
  await page.getByRole("button", { name: /^(Continuar|Iniciar diagnóstico)$/ }).click();
  const lanzar = page.getByRole("button", { name: "Lanzar diagnóstico" });
  await Promise.race([lanzar.waitFor({ state: "visible" }), page.getByRole("link", { name: "Ver tablero" }).waitFor({ state: "visible" }), page.waitForURL(TABLERO)]);
  if (await lanzar.isVisible()) await lanzar.click();
  return { diagnosticoId: await irAlTablero(page), conAnalisis };
}

/** Masivo con un CSV de identificadores (columna Identificador_Vehiculo, como e2e/nuevo-diagnostico.spec.ts). */
export async function sembrarMasivo(page: Page, identificadores: readonly string[], opciones: { nombre: string; conAnalisis?: boolean }) {
  await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
  await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(opciones.nombre);
  const conAnalisis = opciones.conAnalisis ? await elegirConAnalisis(page) : false;
  await page.getByLabel(/archivo de vehículos/i).setInputFiles({
    name: "lote-saneamiento-e2e.csv",
    mimeType: "text/csv",
    buffer: Buffer.from(`Identificador_Vehiculo\n${identificadores.join("\n")}\n`),
  });
  await page.getByRole("button", { name: "Validar archivo" }).click();
  await expect(page.getByRole("region", { name: "Resumen de validación" })).toBeVisible();
  await page.getByRole("button", { name: /^Continuar/ }).click();
  return { diagnosticoId: await irAlTablero(page), conAnalisis };
}

// ---------- Lectura y escritura por el BFF (solo para preparar estados) ----------

export interface VehiculoGrilla {
  id: string;
  placa: string | null;
  vin?: string | null;
  identificador: string;
  terminado: boolean;
  saneamiento?: { estado: string | null; elegible: boolean; motivo_no_elegible: string | null } | null;
}

export async function leerGrilla(page: Page, diagnosticoId: string): Promise<{ modo: string; vehiculos: VehiculoGrilla[] }> {
  const r = await page.request.get(`${API}/diagnosticos/${diagnosticoId}/grilla?desde=0`);
  expect(r.ok(), `GET grilla ${r.status()}`).toBeTruthy();
  return r.json();
}

/** Espera a que terminen los vehículos indicados (todas sus celdas en COMPLETADO o NA). */
export async function esperarTerminados(page: Page, diagnosticoId: string, identificadores: readonly string[], timeout = 120_000) {
  await expect
    .poll(
      async () => {
        const g = await leerGrilla(page, diagnosticoId);
        return identificadores.every((id) => g.vehiculos.some((v) => (v.placa === id || v.vin === id || v.identificador === id) && v.terminado));
      },
      { timeout, intervals: [2_000] },
    )
    .toBe(true);
}

export async function idVehiculo(page: Page, diagnosticoId: string, identificador: string): Promise<string> {
  const g = await leerGrilla(page, diagnosticoId);
  const v = g.vehiculos.find((x) => x.placa === identificador || x.vin === identificador || x.identificador === identificador);
  expect(v, `vehículo ${identificador} en el lote`).toBeTruthy();
  return v!.id;
}

/** PUT …/saneamiento por el BFF con la sesión de la compañía (preparación; el flujo bajo prueba usa la bandera). */
export async function marcarPorApi(page: Page, diagnosticoId: string, vehiculoId: string) {
  const r = await page.request.put(`${API}/diagnosticos/${diagnosticoId}/vehiculos/${vehiculoId}/saneamiento`);
  expect([200, 201], `PUT saneamiento ${r.status()}`).toContain(r.status());
}

/** Saneamiento de una placa en la bandeja del backoffice (sesión FLIT), por estado. */
export async function buscarEnBandeja(bo: Page, placa: string, estado = "SOLICITADO"): Promise<{ id: string; empresa: string }> {
  let item: { id: string; empresa: string } | undefined;
  await expect
    .poll(
      async () => {
        const r = await bo.request.get(`${API}/backoffice/saneamientos?estado=${estado}&placa=${encodeURIComponent(placa)}&pagina=1&tamano=10`);
        if (!r.ok()) return false;
        const lista = (await r.json()) as { saneamientos: { id: string; empresa: string; placa: string | null }[] };
        item = lista.saneamientos.find((s) => s.placa === placa);
        return Boolean(item);
      },
      { timeout: 30_000 },
    )
    .toBe(true);
  return item!;
}

/** POST …/{sid}/tomar con la sesión FLIT (preparación del estado EN_GESTION). */
export async function tomarPorApi(bo: Page, sid: string, empresa: string) {
  const r = await bo.request.post(`${API}/backoffice/saneamientos/${sid}/tomar?empresa=${encodeURIComponent(empresa)}`);
  expect(r.ok(), `POST tomar ${r.status()}`).toBeTruthy();
}

// ---------- Fallos y esperas inyectados ----------

type Fallo = { status: number; code: string; error: string } | "red";

/**
 * Inyecta un fallo en la PRÓXIMA petición que cumpla método y ruta; las demás siguen a la suite real. El cuerpo del error es
 * el `ErrorRegla` del contrato (`code`, `error`). Con `siempre`, falla todas hasta `quitar()`: es lo que conviene en las
 * lecturas que hace un efecto, porque `next dev` (modo estricto) puede pedirlas dos veces al montar.
 */
export async function fallarUnaVez(page: Page, metodo: string, ruta: RegExp, fallo: Fallo, opciones: { siempre?: boolean } = {}) {
  let usado = false;
  const manejador = async (route: Route) => {
    if ((usado && !opciones.siempre) || route.request().method() !== metodo) return route.fallback();
    usado = true;
    if (fallo === "red") return route.abort("failed");
    return route.fulfill({ status: fallo.status, contentType: "application/json", body: JSON.stringify({ code: fallo.code, error: fallo.error }) });
  };
  await page.route(ruta, manejador);
  return {
    usado: () => usado,
    quitar: () => page.unroute(ruta, manejador),
  };
}

/**
 * Retiene las peticiones GET que cumplan `ruta` hasta llamar a `soltar()`; entonces siguen a la suite real. Sirve para la
 * captura de «cargando» y para que el sondeo no adelante un cambio hecho por otra vía.
 */
export async function retener(page: Page, ruta: RegExp) {
  const retenidas: Route[] = [];
  let activo = true;
  const manejador = async (route: Route) => {
    if (!activo || route.request().method() !== "GET") return route.fallback();
    retenidas.push(route);
  };
  await page.route(ruta, manejador);
  return {
    cuantas: () => retenidas.length,
    soltar: async () => {
      activo = false;
      await page.unroute(ruta, manejador);
      for (const r of retenidas.splice(0)) await r.fallback().catch(() => undefined);
    },
  };
}

/** Pulsa y espera la descarga; comprueba la extensión del nombre que da el servidor. */
export async function descargar(page: Page, control: Locator, extension: RegExp): Promise<Download> {
  const [descarga] = await Promise.all([page.waitForEvent("download"), control.click()]);
  expect(descarga.suggestedFilename()).toMatch(extension);
  return descarga;
}

/** El foco está dentro del contenedor. */
export async function focoDentro(contenedor: Locator): Promise<boolean> {
  return contenedor.evaluate((n) => n.contains(document.activeElement));
}

import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { expect, test, type Locator, type Page, type Route } from "@playwright/test";
import { TEMAS, ponerTema, violacionesGraves, type Tema } from "./saneamiento-comun";

/**
 * Apoyo común de los E2E de la oleada «requerimiento.pdf» de Diagnóstico (2026-11-16; HUs #14583, #14584, #14585, #14588).
 * Fuentes: .claude/state/diagnostico-ola-requerimiento-2026-11-16/{spec-ui,contrato-api,security-previa,mapa-carriles}.md.
 *
 * Modo de la API: la suite local real (`scripts/dev/levantar-diagnostico-local.sh simulado`), como el resto de e2e/.
 * `page.route` se usa para (a) variar una respuesta real dentro del contrato 1.6.0 (fuentes del contrato, `cambio_propietario`,
 * `veredicto`, una fuente no pedida), (b) inyectar datos que el front NO debe pintar (SP-19) y (c) simular las rutas de
 * Usuarios mientras be2b no esté integrado (E2E_USUARIOS_MOCK, ver usuarios.spec.ts). Cada uso lo dice en el spec.
 *
 * Capturas: `<id>-<pantalla>-<tema>.png` en `$E2E_SALIDA/capturas` (por defecto
 * `.claude/state/diagnostico-ola-requerimiento-2026-11-16/evidencias/e2e-local/capturas`). Solo placeholders.
 */
const SALIDA = process.env.E2E_SALIDA ?? path.resolve(__dirname, "../../.claude/state/diagnostico-ola-requerimiento-2026-11-16/evidencias/e2e-local");
export const CAPTURAS_REQ = path.join(SALIDA, "capturas");
fs.mkdirSync(CAPTURAS_REQ, { recursive: true });

const BASE = "\\/api\\/v1\\/diagnostico";
export const RUTA_REQ = {
  configuracion: new RegExp(`${BASE}\\/configuracion(\\?|$)`),
  /** GET /diagnosticos/{id}/grilla (también el delta ?desde=). */
  grilla: new RegExp(`${BASE}\\/diagnosticos\\/[0-9a-f-]{36}\\/grilla(\\?|$)`),
  /** GET /backoffice/diagnosticos/{id}/grilla. */
  grillaBackoffice: new RegExp(`${BASE}\\/backoffice\\/diagnosticos\\/[0-9a-f-]{36}\\/grilla(\\?|$)`),
  /** GET /vehiculos (listado de placas, FilaPlaca). */
  vehiculos: new RegExp(`${BASE}\\/vehiculos(\\?|$)`),
  plantilla: new RegExp(`${BASE}\\/plantilla(\\?|$)`),
  carga: new RegExp(`${BASE}\\/diagnosticos\\/[0-9a-f-]{36}\\/carga(\\?|$)`),
  /** Módulo Usuarios (contrato §3.3). */
  usuarios: new RegExp(`${BASE}\\/usuarios(\\?|$)`),
  invitaciones: new RegExp(`${BASE}(\\/backoffice\\/empresas\\/[0-9a-f-]{36})?\\/usuarios\\/invitaciones$`),
  suspension: new RegExp(`${BASE}(\\/backoffice\\/empresas\\/[0-9a-f-]{36})?\\/usuarios\\/[0-9a-f-]{36}\\/suspension$`),
  rol: new RegExp(`${BASE}(\\/backoffice\\/empresas\\/[0-9a-f-]{36})?\\/usuarios\\/[0-9a-f-]{36}\\/rol$`),
  usuariosEmpresa: new RegExp(`${BASE}\\/backoffice\\/empresas\\/[0-9a-f-]{36}\\/usuarios(\\?|$)`),
  empresasUsuarios: new RegExp(`${BASE}\\/backoffice\\/usuarios\\/empresas(\\?|$)`),
} as const;

/** Literal fijo del tooltip de cambio de propietario (contrato §1.2; spec-ui §6.5). El punto final es opcional. */
export const TEXTO_CAMBIO_PROPIETARIO = /^Hubo un cambio de propietario: el documento declarado no coincide con el propietario actual reportado por el RUNT\.?$/;

/** Fuentes con su nombre visible (lib/configuracion.ts FUENTES). */
export const FUENTES_NOMBRE: Record<string, string> = {
  RUNT: "RUNT vehículo",
  RUNT_PROPIETARIO: "RUNT propietario",
  RUNT_LOCATARIO: "RUNT locatario",
  SIMIT_VEHICULO: "SIMIT vehículo",
  SIMIT_PROPIETARIO: "SIMIT propietario",
  SIMIT_LOCATARIO: "SIMIT locatario",
  AVALUO: "Avalúo comercial",
  IMPUESTO_DEPARTAMENTAL: "Impuesto departamental",
};
export const TODAS_LAS_FUENTES = Object.keys(FUENTES_NOMBRE);

// ---------- Reescritura de respuestas reales ----------

/** Reescribe la respuesta REAL (JSON) de las GET que cumplan `ruta`, hasta `quitar()`. */
export async function reescribirGet<T>(page: Page, ruta: RegExp, cambiar: (real: T) => T) {
  const manejador = async (route: Route) => {
    if (route.request().method() !== "GET") return route.fallback();
    const real = await route.fetch();
    if (!real.ok()) return route.fulfill({ response: real });
    await route.fulfill({ response: real, json: cambiar((await real.json()) as T) });
  };
  await page.route(ruta, manejador);
  return { quitar: () => page.unroute(ruta, manejador) };
}

/** Configuración de la compañía con estas fuentes contratadas y todas disponibles (GET /configuracion). */
export async function contratarFuentes(page: Page, habilitadas: readonly string[], disponibles: readonly string[] = TODAS_LAS_FUENTES) {
  return reescribirGet<{ contrato: { fuentes_habilitadas: string[] }; fuentes_disponibles: string[] }>(page, RUTA_REQ.configuracion, (c) => ({
    ...c,
    contrato: { ...c.contrato, fuentes_habilitadas: [...habilitadas] },
    fuentes_disponibles: [...disponibles],
  }));
}

interface FilaGrillaCruda {
  placa: string | null;
  vin?: string | null;
  identificador: string;
  celdas: { fuente: string }[];
  [k: string]: unknown;
}
interface GrillaCruda {
  columnas: Record<string, string[]>;
  vehiculos: FilaGrillaCruda[];
  [k: string]: unknown;
}

export interface VariacionFila {
  cambio_propietario?: boolean | null;
  veredicto?: "VIABLE" | "BLOQUEADO" | null;
  /** SP-19: campos que el front NO debe pintar aunque lleguen (contrato 1.6.0 los retira). */
  basura?: Record<string, unknown>;
}

/**
 * Varía la grilla REAL (cliente o backoffice) dentro del contrato: `cambio_propietario` y `veredicto` por placa, y saca
 * `sinPedir` de `columnas` (y sus celdas) para que esa fuente salga «Sin solicitar».
 */
export async function variarGrilla(page: Page, opciones: { porPlaca?: Record<string, VariacionFila>; sinPedir?: string[]; backoffice?: boolean }) {
  return reescribirGet<GrillaCruda>(page, opciones.backoffice ? RUTA_REQ.grillaBackoffice : RUTA_REQ.grilla, (g) => {
    const sinPedir = new Set(opciones.sinPedir ?? []);
    const columnas = Object.fromEntries(Object.entries(g.columnas ?? {}).map(([k, v]) => [k, v.filter((f) => !sinPedir.has(f))]));
    const vehiculos = (g.vehiculos ?? []).map((v) => {
      const clave = v.placa ?? v.vin ?? v.identificador;
      const cambio = opciones.porPlaca?.[clave];
      const fila: FilaGrillaCruda = { ...v, celdas: v.celdas.filter((c) => !sinPedir.has(c.fuente)) };
      if (cambio) {
        if ("cambio_propietario" in cambio) fila.cambio_propietario = cambio.cambio_propietario;
        if ("veredicto" in cambio) fila.veredicto = cambio.veredicto;
        Object.assign(fila, cambio.basura ?? {});
      }
      return fila;
    });
    return { ...g, columnas, vehiculos };
  });
}

/** Documento ficticio para SP-19 (nunca uno real). La máscara y el número completo no deben aparecer en el DOM. */
export const DOC_FICTICIO = { numero: "1098765432", ultimos: "5432", mascara: "****5432", tipo: "CC" } as const;

/** Campos de documento del contrato 1.5.0 (retirados en 1.6.0) con el documento ficticio, para probar SP-19. */
export const BASURA_DOCUMENTOS = {
  documento_declarado: { tipo_documento: DOC_FICTICIO.tipo, documento_enmascarado: DOC_FICTICIO.mascara, rol: "PROPIETARIO" },
  propietarios: [{ tipo_documento: DOC_FICTICIO.tipo, documento_enmascarado: DOC_FICTICIO.mascara, tipo_propiedad: "PROPIO" }],
  documento_propietario: DOC_FICTICIO.numero,
} as const;

/**
 * SP-19 sobre el DOM: ni el texto visible ni `title`, `aria-*`, `data-*` ni nodos ocultos (incluidos los portales de tooltip)
 * llevan el documento ficticio ni su máscara. Devuelve los hallazgos.
 */
export async function pistasDePii(page: Page, marcas: readonly string[] = [DOC_FICTICIO.numero, DOC_FICTICIO.mascara, `${DOC_FICTICIO.tipo} ${DOC_FICTICIO.mascara}`]) {
  return page.evaluate((m) => {
    const hallazgos: string[] = [];
    const texto = document.body.textContent ?? "";
    for (const x of m) if (texto.includes(x)) hallazgos.push(`texto: ${x}`);
    for (const el of Array.from(document.querySelectorAll("*"))) {
      for (const a of Array.from(el.attributes)) {
        if (!(a.name === "title" || a.name.startsWith("aria-") || a.name.startsWith("data-") || a.name === "alt")) continue;
        for (const x of m) if (a.value.includes(x)) hallazgos.push(`${el.tagName.toLowerCase()}[${a.name}]: ${x}`);
      }
    }
    return hallazgos;
  }, marcas);
}

// ---------- Tooltip (spec-ui §5) ----------

/**
 * Burbuja del tooltip del disparador: el `role=tooltip` al que apunta su `aria-describedby`. Cerrada puede no existir
 * (contenido montado al abrir, SP-19) o estar en el DOM con opacidad 0 (spec-ui §5.3). Devuelve si está abierta y su texto.
 */
export async function estadoTooltip(disparador: Locator): Promise<{ abierto: boolean; texto: string; id: string | null; lado: string | null }> {
  return disparador.evaluate((el) => {
    const ids = (el.getAttribute("aria-describedby") ?? "").split(/\s+/).filter(Boolean);
    for (const id of ids) {
      const t = document.getElementById(id);
      if (!t || t.getAttribute("role") !== "tooltip") continue;
      const s = getComputedStyle(t);
      const r = t.getBoundingClientRect();
      const abierto = !t.hidden && s.display !== "none" && s.visibility !== "hidden" && Number(s.opacity) > 0.5 && r.width > 0 && r.height > 0;
      return { abierto, texto: (t.textContent ?? "").replace(/\s+/g, " ").trim(), id, lado: t.getAttribute("data-lado") ?? t.closest("[data-lado]")?.getAttribute("data-lado") ?? null };
    }
    return { abierto: false, texto: "", id: null, lado: null };
  });
}

export async function esperarTooltip(disparador: Locator, abierto: boolean, timeout = 2_000) {
  await expect.poll(async () => (await estadoTooltip(disparador)).abierto, { timeout, message: `tooltip ${abierto ? "abierto" : "cerrado"}` }).toBe(abierto);
}

// ---------- Tabla por defecto (spec-ui §1) ----------

export interface MedidaTabla {
  radioContenedor: number;
  bordeContenedor: number;
  fondoCabecera: string;
  fondoEsperado: string;
  textoCabecera: string;
  textoEsperado: string;
  letraCabecera: number;
}

/** Mide la receta de §1 sobre una `<table>`: contenedor redondeado con borde, cabecera `--table-header-*`, letra ≥ 12 px. */
export async function medirTabla(tabla: Locator): Promise<MedidaTabla> {
  return tabla.evaluate((t) => {
    // Color resuelto de una variable CSS en el contexto del elemento (respeta la clase `dark`).
    const resolver = (el: Element, prop: string, valor: string) => {
      const sonda = document.createElement("span");
      sonda.style.setProperty(prop, valor);
      sonda.style.position = "absolute";
      el.appendChild(sonda);
      const c = getComputedStyle(sonda).getPropertyValue(prop);
      sonda.remove();
      return c;
    };
    let cont: HTMLElement | null = t.parentElement;
    while (cont && cont !== document.body) {
      const s = getComputedStyle(cont);
      if (parseFloat(s.borderTopLeftRadius) > 0 && parseFloat(s.borderTopWidth) > 0) break;
      cont = cont.parentElement;
    }
    const sc = cont && cont !== document.body ? getComputedStyle(cont) : null;
    const th = t.querySelector("thead th") as HTMLElement | null;
    const sth = th ? getComputedStyle(th) : null;
    return {
      radioContenedor: sc ? parseFloat(sc.borderTopLeftRadius) : 0,
      bordeContenedor: sc ? parseFloat(sc.borderTopWidth) : 0,
      fondoCabecera: sth?.backgroundColor ?? "",
      fondoEsperado: th ? resolver(th.parentElement!, "background-color", "var(--table-header-bg)") : "",
      textoCabecera: sth?.color ?? "",
      textoEsperado: th ? resolver(th.parentElement!, "color", "var(--table-header-fg)") : "",
      letraCabecera: sth ? parseFloat(sth.fontSize) : 0,
    };
  });
}

/** Aserciones blandas de la tabla por defecto en claro y oscuro (A7.1/A7.2); deja el tema claro. */
export async function esperarTablaPorDefecto(page: Page, tabla: Locator, nombre: string) {
  await expect(tabla, `${nombre}: tabla visible`).toBeVisible();
  for (const tema of TEMAS) {
    await ponerTema(page, tema);
    await page.waitForTimeout(150);
    const m = await medirTabla(tabla);
    expect.soft(m.radioContenedor, `${nombre} (${tema}): contenedor rounded-xl`).toBeGreaterThanOrEqual(12);
    expect.soft(m.bordeContenedor, `${nombre} (${tema}): contenedor con borde`).toBeGreaterThanOrEqual(1);
    expect.soft(m.fondoCabecera, `${nombre} (${tema}): fondo de cabecera = --table-header-bg`).toBe(m.fondoEsperado);
    expect.soft(m.textoCabecera, `${nombre} (${tema}): texto de cabecera = --table-header-fg`).toBe(m.textoEsperado);
    expect.soft(m.letraCabecera, `${nombre} (${tema}): cabecera ≥ 12 px (T4)`).toBeGreaterThanOrEqual(12);
  }
  await ponerTema(page, "claro");
}

// ---------- Contraste (toggle AA) ----------

function rgb(c: string): [number, number, number, number] {
  const m = c.match(/rgba?\(([^)]+)\)/);
  if (!m) return [0, 0, 0, 0];
  const p = m[1].split(/[ ,/]+/).filter(Boolean).map(Number);
  return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1];
}
function lum([r, g, b]: [number, number, number, number]) {
  const f = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
}
/** Contraste WCAG entre dos colores `rgb()` opacos. */
export function contraste(a: string, b: string): number {
  const [la, lb] = [lum(rgb(a)), lum(rgb(b))].sort((x, y) => y - x);
  return (la + 0.05) / (lb + 0.05);
}

/** Color resuelto de `var(--token)` dentro de `el` (respeta la clase `dark`). */
export async function colorDeToken(el: Locator, token: string): Promise<string> {
  return el.evaluate((n, t) => {
    const sonda = document.createElement("span");
    sonda.style.color = `var(${t})`;
    sonda.style.position = "absolute";
    (n.parentElement ?? n).appendChild(sonda);
    const c = getComputedStyle(sonda).color;
    sonda.remove();
    return c;
  }, token);
}

// ---------- Dock (spec-ui §9) ----------

export interface LadosDock {
  izquierda: string[];
  derecha: string[];
  espaciadores: number;
}

/** Entradas del dock a cada lado del botón de inicio, en orden, y espaciadores `span[aria-hidden]` hijos de la cápsula. */
export async function ladosDock(page: Page, etiquetaInicio = "Inicio Diagnóstico"): Promise<LadosDock> {
  const nav = page.getByRole("navigation", { name: "Navegación principal" }).first();
  await expect(nav).toBeVisible();
  return nav.evaluate((n, inicio) => {
    const hijos = Array.from(n.children) as HTMLElement[];
    const nombre = (h: HTMLElement) => {
      const control = h.matches("a,button") ? h : (h.querySelector(":scope > a, :scope > button") as HTMLElement | null);
      return (control?.getAttribute("aria-label") ?? control?.textContent ?? "").split(",")[0].trim();
    };
    const iFab = hijos.findIndex((h) => h.getAttribute("aria-label") === inicio);
    const esEspaciador = (h: HTMLElement) => h.tagName === "SPAN" && h.getAttribute("aria-hidden") === "true";
    const entradas = (xs: HTMLElement[]) => xs.filter((h) => !esEspaciador(h)).map(nombre);
    return {
      izquierda: entradas(hijos.slice(0, iFab)),
      derecha: entradas(hijos.slice(iFab + 1)),
      espaciadores: hijos.filter(esEspaciador).length,
    };
  }, etiquetaInicio);
}

/** Orden de catálogo del dock de Diagnóstico (spec-ui §9). */
export const ORDEN_DOCK = ["Diagnósticos", "Reportes", "Backoffice", "Usuarios", "Configuración"] as const;

/** Reparto equilibrado: ceil(n/2) a la izquierda, el resto a la derecha, en orden de catálogo y sin espaciadores. */
export function esperarDockEquilibrado(lados: LadosDock, contexto: string) {
  const todas = [...lados.izquierda, ...lados.derecha];
  expect.soft(lados.espaciadores, `${contexto}: sin espaciadores`).toBe(0);
  expect.soft(lados.izquierda.length, `${contexto}: ceil(n/2) a la izquierda (${todas.join(" · ")})`).toBe(Math.ceil(todas.length / 2));
  const posiciones = todas.map((e) => ORDEN_DOCK.findIndex((o) => e.startsWith(o)));
  expect.soft(posiciones, `${contexto}: entradas conocidas`).not.toContain(-1);
  expect.soft([...posiciones].sort((a, b) => a - b), `${contexto}: orden de catálogo`).toEqual(posiciones);
}

// ---------- Captura de evidencia (PDF de evidencias de la oleada) ----------

/**
 * Captura de un paso clave del AC con nombre estable `<spec>-<nn>-<paso>.png` en `testInfo.outputPath(...)` (y adjunta al
 * reporte HTML). No existe QaCapture en e2e/ (vive solo como plantilla temporal de playwright-runner), así que este es el
 * helper mínimo. Solo datos sembrados E2E y placeholders: nunca PII real.
 */
export async function capturar(page: Page, spec: string, nn: number, paso: string, opciones: { paginaCompleta?: boolean } = {}) {
  const nombre = `${spec}-${String(nn).padStart(2, "0")}-${paso}.png`;
  const info = test.info();
  const ruta = info.outputPath(nombre);
  await page.screenshot({ path: ruta, fullPage: opciones.paginaCompleta ?? false });
  await info.attach(nombre, { path: ruta, contentType: "image/png" });
  return ruta;
}

// ---------- Tema + axe + captura ----------

export async function revisarTemasReq(page: Page, pantalla: string, opciones: { captura: string; paginaCompleta?: boolean; axe?: boolean }) {
  for (const tema of TEMAS as readonly Tema[]) {
    await ponerTema(page, tema);
    await page.waitForTimeout(250);
    await page.screenshot({ path: path.join(CAPTURAS_REQ, `${opciones.captura}-${pantalla}-${tema}.png`), fullPage: opciones.paginaCompleta ?? true });
    if (opciones.axe !== false) {
      const graves = await violacionesGraves(page);
      expect.soft(graves, `axe en ${pantalla} (${tema}): violaciones serias o críticas`).toEqual([]);
    }
  }
  await ponerTema(page, "claro");
}

// ---------- .xlsx mínimo (sin dependencias) ----------

/** Texto de todas las entradas XML de un .xlsx (zip): cabeceras y hoja de ayuda de la plantilla. */
export function textoDeXlsx(buffer: Buffer): string {
  // Directorio central: firma 0x02014b50; se localiza desde el registro de fin (0x06054b50).
  let fin = buffer.length - 22;
  while (fin >= 0 && buffer.readUInt32LE(fin) !== 0x06054b50) fin--;
  expect(fin, "fin del directorio central del .xlsx").toBeGreaterThanOrEqual(0);
  const total = buffer.readUInt16LE(fin + 10);
  let p = buffer.readUInt32LE(fin + 16);
  const partes: string[] = [];
  for (let i = 0; i < total; i++) {
    const metodo = buffer.readUInt16LE(p + 10);
    const comprimido = buffer.readUInt32LE(p + 20);
    const largoNombre = buffer.readUInt16LE(p + 28);
    const largoExtra = buffer.readUInt16LE(p + 30);
    const largoComentario = buffer.readUInt16LE(p + 32);
    const local = buffer.readUInt32LE(p + 42);
    const nombre = buffer.toString("utf8", p + 46, p + 46 + largoNombre);
    p += 46 + largoNombre + largoExtra + largoComentario;
    if (!nombre.endsWith(".xml")) continue;
    const inicio = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
    const datos = buffer.subarray(inicio, inicio + comprimido);
    partes.push((metodo === 8 ? zlib.inflateRawSync(datos) : datos).toString("utf8"));
  }
  return partes.join("\n");
}

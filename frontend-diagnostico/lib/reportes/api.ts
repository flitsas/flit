// Descargas del tablero (HU #13851, F07-H6). Contratos de contracts/openapi/diagnostico.v1.yaml (tag Reportes):
// GET /diagnosticos/{id}/vehiculos/{vid}/pdf (#13848), GET /diagnosticos/{id}/excel (#13849) y el ZIP en segundo plano
// (#13850): POST /diagnosticos/{id}/evidencias {tipo:"ZIP"} → GET …/evidencias/{eid} hasta LISTA o ERROR → GET …/archivo.
// Todo pasa por el BFF (/api/v1/*), que reenvía el cuerpo binario y el Content-Disposition. La API vuelve a exigir
// `diagnostico.reportes.descargar` y la regla de emisión (EmisionPdf.Puede); el frontend solo la anticipa.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";
import { API_PREFIX, ApiError, apiFetch } from "@/lib/api";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { MENSAJE_RANGO_GENERICO } from "@/lib/estadisticas/periodo";
import type { EstadoCelda, FilaVehiculo } from "@/lib/tablero/api";
import type { CatalogoReportesDto, FormatoExportacion, PaginaReporteDto, TipoReporte } from "./tipos";

export type EstadoZip = "PENDIENTE" | "GENERANDO" | "LISTA" | "ERROR";

/** Cuerpo del 200/202 del POST y del GET de estado (schema `EstadoEvidencia`). */
export interface EstadoEvidencia {
  id: string;
  tipo: "ZIP";
  estado: EstadoZip;
  /** Vehículos con PDF (esperados mientras está en cola; los del archivo cuando LISTA). */
  vehiculos_incluidos: number;
  /** Vehículos fuera por no haber terminado (salen en el índice del ZIP). */
  vehiculos_pendientes: number;
  creado_en: string;
  generado_en: string | null;
  /** Solo con ERROR; es técnico y no se muestra. */
  tipo_error: string | null;
}

/** Cada cuánto se lee el estado del ZIP mientras está en cola o generándose. */
export const INTERVALO_SONDEO_ZIP_MS = 3000;

const ruta = (id: string, sufijo: string) => `/diagnosticos/${encodeURIComponent(id)}${sufijo}`;

/** URL del BFF del PDF de un vehículo (para el `href` del enlace con `download`). */
export const urlPdfVehiculo = (id: string, vid: string): string => `${API_PREFIX}${ruta(id, `/vehiculos/${encodeURIComponent(vid)}/pdf`)}`;

/** URL del BFF del Excel del lote. */
export const urlExcelLote = (id: string): string => `${API_PREFIX}${ruta(id, "/excel")}`;

/** URL del BFF del archivo de un ZIP LISTA. */
export const urlArchivoZip = (id: string, eid: string): string => `${API_PREFIX}${ruta(id, `/evidencias/${encodeURIComponent(eid)}/archivo`)}`;

/** POST /diagnosticos/{id}/evidencias: 202 (en cola, o el mismo pedido en curso) o 200 (ya hay uno LISTA igual). */
export function pedirZip(id: string): Promise<EstadoEvidencia> {
  return apiFetch<EstadoEvidencia>(ruta(id, "/evidencias"), { method: "POST", body: { tipo: "ZIP" } });
}

/** GET /diagnosticos/{id}/evidencias/{eid}: estado del ZIP pedido. */
export function estadoZip(id: string, eid: string, signal?: AbortSignal): Promise<EstadoEvidencia> {
  return apiFetch<EstadoEvidencia>(ruta(id, `/evidencias/${encodeURIComponent(eid)}`), { signal });
}

/** Nombre del archivo del Content-Disposition: `filename*=UTF-8''…` y si no `filename=…`; si no, el de por defecto. */
export function nombreArchivo(contentDisposition: string | null, porDefecto: string): string {
  if (!contentDisposition) return porDefecto;
  const extendido = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(contentDisposition);
  if (extendido) {
    try {
      return decodeURIComponent(extendido[1].trim());
    } catch {
      // Codificación inválida: se intenta con el nombre simple.
    }
  }
  const simple = /filename\s*=\s*"?([^";]+)"?/i.exec(contentDisposition);
  return simple?.[1].trim() || porDefecto;
}

/** Entrega el blob al navegador como descarga con su nombre y libera la URL del blob (SP-18). */
export function guardar(blob: Blob, nombre: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Se libera después del clic: algunos navegadores leen la URL en el siguiente ciclo.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Descarga un archivo del BFF (PDF, Excel o ZIP) por blob, con el nombre del Content-Disposition. Un error HTTP lanza
 * `ApiError` con el `code` del cuerpo (409 VEHICULO_NO_TERMINADO, 404 NO_ENCONTRADO…) y no descarga nada.
 */
export async function descargarArchivo(url: string, porDefecto: string): Promise<string> {
  if (isReauthenticating()) return new Promise<string>(() => {});
  const respuesta = await fetch(url, { method: "GET", cache: "no-store" });
  if (!respuesta.ok) {
    if (respuesta.status === 401) reauthenticate({ silent: true });
    const datos = await leerError(respuesta);
    throw new ApiError(respuesta.status, datos?.error ?? "No se pudo descargar el archivo.", datos?.code);
  }
  const nombre = nombreArchivo(respuesta.headers.get("Content-Disposition"), porDefecto);
  guardar(await respuesta.blob(), nombre);
  return nombre;
}

async function leerError(respuesta: Response): Promise<{ code?: string; error?: string } | null> {
  try {
    const texto = await respuesta.text();
    return texto ? (JSON.parse(texto) as { code?: string; error?: string }) : null;
  } catch {
    return null;
  }
}

const MENSAJES: Record<string, string> = {
  VEHICULO_NO_TERMINADO: "Este vehículo todavía tiene consultas en curso. El PDF estará disponible cuando terminen.",
  SIN_VEHICULOS_TERMINADOS: "Aún no hay vehículos terminados para armar el ZIP. Inténtalo cuando termine alguno.",
  LOTE_SIN_LANZAR: "El lote todavía no se ha lanzado: aún no hay nada que descargar.",
  EVIDENCIA_NO_LISTA: "El ZIP todavía se está preparando. Espera a que quede listo.",
};

/** Texto para el usuario de un error de descarga (nunca el código técnico). */
export function mensajeDescarga(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && MENSAJES[error.code]) return MENSAJES[error.code];
    if (error.status === 404) return "No encontramos este diagnóstico o ya no tienes acceso a él.";
    if (error.status === 403) return "No tienes permiso para descargar reportes.";
  }
  return error instanceof Error ? error.message : "No se pudo descargar el archivo.";
}

const CERRADAS: ReadonlySet<EstadoCelda> = new Set(["COMPLETADO", "NA", "ERROR"]);

/**
 * Misma regla que `EmisionPdf.Puede` del backend (R-7/Q7): el vehículo terminó (todas sus celdas en COMPLETADO o N/A,
 * `terminado` de la grilla) o el lote está INCOMPLETO y todas sus celdas están cerradas (las en ERROR salen «consulta no
 * disponible»). Sin celdas, no. El servidor decide: si responde 409, se muestra su mensaje.
 */
export function puedeEmitirPdf(estadoLote: EstadoDiagnostico, vehiculo: FilaVehiculo): boolean {
  if (vehiculo.terminado) return true;
  return estadoLote === "INCOMPLETO" && vehiculo.celdas.length > 0 && vehiculo.celdas.every((c) => CERRADAS.has(c.estado));
}

// ── Módulo de reportes (ola Diagnóstico UX, HU #14580; contrato-api.md §2–§5) ──────────────────────────────────────
// Compañía: /reportes, /reportes/{tipo}[/excel|/pdf]. Vista global (capacidad verEstadisticasGlobales, Q4):
// /backoffice/reportes, /backoffice/reportes/{tipo}/global[/excel|/pdf] y /backoffice/reportes/{tipo}[/excel|/pdf]?empresa=.
// La API vuelve a exigir cada permiso y audita cada petición (accesos_reporte) antes de responder.

export type AmbitoReporte = { tipo: "COMPANIA" } | { tipo: "GLOBAL" } | { tipo: "EMPRESA"; empresa: string };

export interface ConsultaReporte {
  desde: string;
  hasta: string;
  /** Enums del catálogo (clave → valores; los repetibles van como parámetro repetido). */
  filtros: Record<string, string[]>;
  /** Prefijo de placa; viaja a la API, nunca a la URL de la pantalla (SP-18). */
  placa?: string;
  pagina?: number;
  tamano?: number;
}

/** Ámbito de la pantalla: la compañía propia o, en la vista global, todas ("") o una compañía elegida. */
export function ambitoReporte(vistaGlobal: boolean, empresa: string): AmbitoReporte {
  if (!vistaGlobal) return { tipo: "COMPANIA" };
  return empresa ? { tipo: "EMPRESA", empresa } : { tipo: "GLOBAL" };
}

export function rutaReporte(ambito: AmbitoReporte, tipo: TipoReporte, formato?: FormatoExportacion): string {
  const sufijo = formato ? `/${formato}` : "";
  const t = encodeURIComponent(tipo);
  if (ambito.tipo === "COMPANIA") return `/reportes/${t}${sufijo}`;
  if (ambito.tipo === "GLOBAL") return `/backoffice/reportes/${t}/global${sufijo}`;
  return `/backoffice/reportes/${t}${sufijo}`;
}

/** Placa como la normaliza el servidor para el prefijo: mayúsculas, sin espacios ni guiones, hasta 20 caracteres. */
export function normalizarPlaca(placa: string): string {
  return placa.replace(/[\s-]+/g, "").toUpperCase().slice(0, 20);
}

/** Query de la petición; con `paginada` false (exportaciones) no lleva pagina ni tamano. */
export function consultaComoQuery(ambito: AmbitoReporte, consulta: ConsultaReporte, paginada: boolean): URLSearchParams {
  const q = new URLSearchParams({ desde: consulta.desde, hasta: consulta.hasta });
  for (const [clave, valores] of Object.entries(consulta.filtros)) for (const v of valores) q.append(clave, v);
  const placa = normalizarPlaca(consulta.placa ?? "");
  if (placa) q.set("placa", placa);
  if (paginada) {
    q.set("pagina", String(consulta.pagina ?? 1));
    q.set("tamano", String(consulta.tamano ?? 25));
  }
  if (ambito.tipo === "EMPRESA") q.set("empresa", ambito.empresa);
  return q;
}

export function obtenerCatalogo(vistaGlobal: boolean, signal?: AbortSignal): Promise<CatalogoReportesDto> {
  return apiFetch<CatalogoReportesDto>(vistaGlobal ? "/backoffice/reportes" : "/reportes", { signal, cache: "no-store" });
}

export function obtenerReporte(ambito: AmbitoReporte, tipo: TipoReporte, consulta: ConsultaReporte, signal?: AbortSignal): Promise<PaginaReporteDto> {
  return apiFetch<PaginaReporteDto>(`${rutaReporte(ambito, tipo)}?${consultaComoQuery(ambito, consulta, true)}`, { signal, cache: "no-store" });
}

/** Error de una exportación: 422 trae `filas` y `maximo`; 429, `retryAfter`. */
export class ErrorDescargaReporte extends ApiError {
  constructor(
    status: number,
    message: string,
    code?: string,
    readonly filas?: number,
    readonly maximo?: number,
    retryAfter?: number,
  ) {
    super(status, message, code, undefined, retryAfter);
    this.name = "ErrorDescargaReporte";
  }
}

/**
 * Exporta TODO el filtro (sin paginación) en Excel o PDF: GET por el BFF con no-store, Blob con el nombre del
 * Content-Disposition (nunca uno armado con datos de la fila) y la URL del blob liberada. Un error HTTP no descarga nada.
 */
export async function descargarReporte(ambito: AmbitoReporte, tipo: TipoReporte, consulta: ConsultaReporte, formato: FormatoExportacion): Promise<string> {
  // Con la sesión renovándose no se pide nada; se rechaza (no una promesa colgada) para que el botón salga de «Generando…».
  if (isReauthenticating()) throw new ErrorDescargaReporte(401, "Sesión en renovación.", "SESION_RENOVANDO");
  const url = `${API_PREFIX}${rutaReporte(ambito, tipo, formato)}?${consultaComoQuery(ambito, consulta, false)}`;
  const respuesta = await fetch(url, { method: "GET", cache: "no-store" });
  if (!respuesta.ok) {
    if (respuesta.status === 401) reauthenticate({ silent: true });
    const datos = (await leerError(respuesta)) as { code?: string; filas?: unknown; maximo?: unknown } | null;
    const espera = Number.parseInt(respuesta.headers.get("Retry-After") ?? "", 10);
    throw new ErrorDescargaReporte(
      respuesta.status,
      "No se pudo exportar el reporte.",
      datos?.code,
      typeof datos?.filas === "number" ? datos.filas : undefined,
      typeof datos?.maximo === "number" ? datos.maximo : undefined,
      Number.isFinite(espera) && espera >= 0 ? espera : undefined,
    );
  }
  const nombre = nombreArchivo(respuesta.headers.get("Content-Disposition"), `diagnostico-reporte-${tipo}.${formato === "excel" ? "xlsx" : "pdf"}`);
  guardar(await respuesta.blob(), nombre);
  return nombre;
}

export const NOMBRE_FORMATO: Record<FormatoExportacion, string> = { excel: "Excel", pdf: "PDF" };
const miles = new Intl.NumberFormat("es-CO");

/** AP-9: texto fijo de un error de exportación (nunca el mensaje del servidor). */
export function mensajeDescargaReporte(error: unknown, formato: FormatoExportacion): string {
  if (error instanceof ApiError) {
    if (error.code === "REPORTE_DEMASIADO_GRANDE" || error.status === 422) {
      const e = error as ErrorDescargaReporte;
      return e.filas !== undefined && e.maximo !== undefined
        ? `El reporte tiene ${miles.format(e.filas)} registros y el ${NOMBRE_FORMATO[formato]} admite ${miles.format(e.maximo)}. Acota el rango o los filtros.`
        : "El reporte tiene más registros de los que admite el formato. Acota el rango o los filtros.";
    }
    if (error.code === "EXPORTACION_EN_CURSO" || error.status === 429) {
      return error.retryAfter !== undefined
        ? `Ya hay una exportación en curso. Espera ${error.retryAfter} segundos e intenta de nuevo.`
        : "Ya hay una exportación en curso. Espera unos segundos e intenta de nuevo.";
    }
    if (error.code === "SESION_RENOVANDO") return "Tu sesión se está renovando. Intenta de nuevo en unos segundos.";
    if (error.status === 403) return "No tienes permiso para descargar reportes.";
    if (error.code === "RANGO_INVALIDO") return MENSAJE_RANGO_GENERICO;
    if (error.code === "EMPRESA_NO_ENCONTRADA") return "Esa compañía no tiene Diagnóstico activo.";
  }
  return "No se pudo exportar el reporte. Intenta de nuevo en un momento.";
}

/** AP-9: textos fijos por `code` del reporte en pantalla. */
export const ERRORES_REPORTE: Readonly<Record<string, string>> = {
  // Sin la causa en el cuerpo; con el rango a mano, `mensajeRangoInvalido` (periodo.ts) la nombra.
  RANGO_INVALIDO: MENSAJE_RANGO_GENERICO,
  FILTRO_INVALIDO: "Revisa los filtros del reporte.",
  PAGINACION_INVALIDA: "Acota el filtro para ver más registros.",
  REPORTE_NO_ENCONTRADO: "Este reporte no está disponible.",
  EMPRESA_NO_ENCONTRADA: "Esa compañía no tiene Diagnóstico activo.",
  IDENTIDAD_NO_DISPONIBLE: "El directorio de compañías no responde. Intenta de nuevo en unos segundos.",
};

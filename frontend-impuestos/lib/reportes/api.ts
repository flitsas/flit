// Reportes de Impuestos (impuestos.v1.yaml 1.0.0, tag Reportes): GET /reportes/{tipo} paginado y GET /reportes/{tipo}/excel
// y /pdf con los mismos filtros, sin paginación. Todas admiten `empresa` (solo Super Admin). La exportación baja por blob
// para que un 422 TOPE_EXPORTACION se explique en pantalla en vez de guardarse como archivo.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";
import { API_PREFIX, ApiError, apiFetch } from "@/lib/api";
import { conEmpresa } from "@/lib/empresaSeleccionada";
import { ERRORES_EMPRESA, conTope } from "@/lib/errores";
import type { Rango } from "@/lib/estadisticas/tipos";
import { reporteDe, type FiltroReporte, type FormatoExportacion, type TipoReporte } from "./catalogo";

export interface FiltrosReporte {
  rango: Rango;
  tipoLiquidacion: "" | "INDIVIDUAL" | "MASIVA";
  estado: string;
  departamento: string;
  usuario: string;
  placa: string;
}

export interface ColumnaReporte {
  clave: string;
  etiqueta: string;
}

export type FilaReporte = Record<string, unknown>;

export interface PaginaReporte {
  tipo: TipoReporte;
  columnas: ColumnaReporte[];
  items: FilaReporte[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Departamento {
  codigo: string;
  nombre: string;
}

export const TAMANO_PAGINA_REPORTE = 50;

export const filtrosVacios = (rango: Rango): FiltrosReporte => ({ rango, tipoLiquidacion: "", estado: "", departamento: "", usuario: "", placa: "" });

/** ¿Hay algún filtro aparte del periodo? (vacío «por filtro» frente a «sin datos»). */
export function hayFiltros(f: FiltrosReporte, tipo: TipoReporte): boolean {
  return Object.values(valoresDeFiltros(f, tipo)).some(Boolean);
}

/** Valores de los filtros que aplican al reporte (los demás no viajan). */
function valoresDeFiltros(f: FiltrosReporte, tipo: TipoReporte): Partial<Record<string, string>> {
  const aplica = new Set<FiltroReporte>(reporteDe(tipo).filtros);
  return {
    tipoLiquidacion: aplica.has("tipo") ? f.tipoLiquidacion : "",
    estado: aplica.has("estado") ? f.estado : "",
    departamento: aplica.has("departamento") ? f.departamento : "",
    usuario: aplica.has("usuario") ? f.usuario : "",
    placa: aplica.has("placa") ? f.placa.trim() : "",
  };
}

function parametros(f: FiltrosReporte, tipo: TipoReporte, pagina?: { page: number; pageSize: number }): URLSearchParams {
  const params = new URLSearchParams({ desde: f.rango.desde, hasta: f.rango.hasta });
  for (const [k, v] of Object.entries(valoresDeFiltros(f, tipo))) if (v) params.set(k, v);
  if (pagina) {
    params.set("page", String(pagina.page));
    params.set("pageSize", String(pagina.pageSize));
  }
  return params;
}

export function obtenerReporte(
  tipo: TipoReporte,
  filtros: FiltrosReporte,
  pagina: { page: number; pageSize: number },
  empresa: string | null,
  signal?: AbortSignal,
): Promise<PaginaReporte> {
  return apiFetch<PaginaReporte>(`/reportes/${tipo}?${parametros(filtros, tipo, pagina).toString()}`, { signal, empresa });
}

/**
 * URL del BFF de la exportación. El contrato lista solo desde/hasta/placa/empresa en /excel y /pdf, pero su descripción
 * dice «mismos filtros que el reporte»: se envían todos los del reporte (hueco reportado en el informe de M17).
 */
export function urlExportacion(tipo: TipoReporte, formato: FormatoExportacion, filtros: FiltrosReporte, empresa: string | null): string {
  return `${API_PREFIX}${conEmpresa(`/reportes/${tipo}/${formato}?${parametros(filtros, tipo).toString()}`, empresa)}`;
}

/** Departamentos del proveedor (GET /fuentes) para el filtro «Departamento». */
export async function listarDepartamentos(signal?: AbortSignal): Promise<Departamento[]> {
  const datos = await apiFetch<{ items: Departamento[] }>("/fuentes", { signal });
  return datos?.items ?? [];
}

/** x-flit-literales.errores de las rutas de reportes. */
export const ERRORES_REPORTES: Readonly<Record<string, string>> = {
  RANGO_INVALIDO: "El rango de fechas no es válido.",
  FILTRO_INVALIDO: "Un filtro no es válido.",
  PAGINACION_INVALIDA: "La paginación no es válida.",
  REPORTE_NO_ENCONTRADO: "El reporte no existe.",
  ...ERRORES_EMPRESA,
  TOPE_EXPORTACION: "El reporte supera el máximo de {tope} filas para exportar: ajusta los filtros.",
  DEMASIADAS_SOLICITUDES: "Hiciste demasiadas solicitudes. Espera un momento e intenta de nuevo.",
};

export const MENSAJE_EXPORTACION_FALLIDA = "No se pudo generar el archivo. Intenta de nuevo.";

/** Texto bajo los botones de un error de exportación: el literal del `code` (con {tope}) o el genérico. */
export function mensajeExportacion(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && Object.prototype.hasOwnProperty.call(ERRORES_REPORTES, error.code)) return conTope(ERRORES_REPORTES[error.code], error.detalle?.tope);
    if (error.status === 403) return "No tienes permiso para esta acción.";
  }
  return MENSAJE_EXPORTACION_FALLIDA;
}

/** Baja el archivo por blob con el nombre del Content-Disposition; un error del servidor sale como ApiError. */
export async function descargarArchivo(url: string, porDefecto: string): Promise<string> {
  if (isReauthenticating()) return new Promise<string>(() => {});
  const respuesta = await fetch(url, { method: "GET", cache: "no-store" });
  if (!respuesta.ok) {
    if (respuesta.status === 401) reauthenticate({ silent: true });
    const datos = await leerError(respuesta);
    throw new ApiError(respuesta.status, MENSAJE_EXPORTACION_FALLIDA, typeof datos?.code === "string" ? datos.code : undefined, datos ?? undefined);
  }
  const nombre = nombreArchivo(respuesta.headers.get("Content-Disposition"), porDefecto);
  guardar(await respuesta.blob(), nombre);
  return nombre;
}

async function leerError(respuesta: Response): Promise<Record<string, unknown> | null> {
  try {
    const texto = await respuesta.text();
    return texto ? (JSON.parse(texto) as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

function nombreArchivo(disposicion: string | null, porDefecto: string): string {
  if (!disposicion) return porDefecto;
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposicion);
  if (utf8) {
    try {
      return decodeURIComponent(utf8[1].trim());
    } catch {
      return porDefecto;
    }
  }
  const simple = /filename="?([^";]+)"?/i.exec(disposicion);
  return simple ? simple[1].trim() : porDefecto;
}

function guardar(blob: Blob, nombre: string) {
  const href = URL.createObjectURL(blob);
  const enlace = document.createElement("a");
  enlace.href = href;
  enlace.download = nombre;
  enlace.rel = "noopener";
  document.body.appendChild(enlace);
  enlace.click();
  enlace.remove();
  setTimeout(() => URL.revokeObjectURL(href), 0);
}

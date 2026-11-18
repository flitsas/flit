// Estadísticas de Diagnóstico (contrato-api.md §1). Tres rutas según el ámbito, todas GET con desde y hasta obligatorios:
//   - compañía:            GET /estadisticas                     (diagnostico.diagnosticos.ver; nunca lleva ?empresa=)
//   - backoffice global:   GET /backoffice/estadisticas/global   (capacidad verEstadisticasGlobales)
//   - backoffice compañía: GET /backoffice/estadisticas?empresa= (ídem; 404 EMPRESA_NO_ENCONTRADA si no está habilitada)
// La API vuelve a exigir cada permiso; el front solo elige la ruta.
import { ApiError, apiFetch } from "@/lib/api";
import { mensajeErrorFijo } from "@/lib/errores";
import { MENSAJE_RANGO_GENERICO } from "./periodo";
import type { EstadisticasDto, Rango } from "./tipos";

export type AmbitoConsulta = { tipo: "COMPANIA" } | { tipo: "GLOBAL" } | { tipo: "EMPRESA"; empresa: string };

/** Ámbito de la vista: la compañía propia, o en la vista global «todas» (empresa "") o una compañía elegida. */
export function ambitoDe(vistaGlobal: boolean, empresa: string): AmbitoConsulta {
  if (!vistaGlobal) return { tipo: "COMPANIA" };
  return empresa ? { tipo: "EMPRESA", empresa } : { tipo: "GLOBAL" };
}

export function rutaEstadisticas(ambito: AmbitoConsulta, { desde, hasta }: Rango): string {
  const q = new URLSearchParams({ desde, hasta });
  if (ambito.tipo === "COMPANIA") return `/estadisticas?${q}`;
  if (ambito.tipo === "GLOBAL") return `/backoffice/estadisticas/global?${q}`;
  q.set("empresa", ambito.empresa);
  return `/backoffice/estadisticas?${q}`;
}

export function obtenerEstadisticas(ambito: AmbitoConsulta, rango: Rango, signal?: AbortSignal): Promise<EstadisticasDto> {
  return apiFetch<EstadisticasDto>(rutaEstadisticas(ambito, rango), { signal, cache: "no-store" });
}

/** AP-9: textos fijos por `code` (nunca el mensaje del servidor). */
export const ERRORES_ESTADISTICAS: Readonly<Record<string, string>> = {
  // Sin la causa en el cuerpo; con el rango a mano, `mensajeRangoInvalido` (periodo.ts) la nombra.
  RANGO_INVALIDO: MENSAJE_RANGO_GENERICO,
  FILTRO_INVALIDO: "Revisa las fechas del periodo.",
  EMPRESA_NO_ENCONTRADA: "Esa compañía no tiene Diagnóstico activo.",
  EMPRESA_REQUERIDA: "Elige una compañía.",
  IDENTIDAD_NO_DISPONIBLE: "El directorio de compañías no responde. Intenta de nuevo en unos segundos.",
};

/** El error es del rango (se pinta junto a «Hasta», no como error de la vista). */
export function esErrorDeRango(error: unknown): boolean {
  return error instanceof ApiError && error.status === 400 && (error.code === "RANGO_INVALIDO" || error.code === "FILTRO_INVALIDO");
}

export function mensajeErrorEstadisticas(error: unknown): string {
  return mensajeErrorFijo(error, "Diagnóstico no respondió. Intenta de nuevo en un momento.", ERRORES_ESTADISTICAS);
}

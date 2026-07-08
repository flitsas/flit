// Traducción de fallos de la API de métricas a mensajes accionables en español
// (Reportes 2.0, HU-C). Único punto de verdad para las 5 pestañas.
import { ApiError } from "@/lib/api/types";

/** Describe un error de carga de métricas para mostrarlo en UiStateBoundary. */
export function describeMetricsError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return "El rango de fechas no es válido o falta la compañía.";
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
    if (error.status === 403) return "No tienes acceso a las métricas de esa compañía.";
    // Transición Reportes 2.0: el endpoint puede no estar desplegado todavía.
    if (error.status === 404) return "Este reporte aún no está disponible en el servidor.";
  }
  return "No se pudieron cargar las métricas.";
}

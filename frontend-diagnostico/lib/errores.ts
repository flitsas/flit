// SF-6 (HU #14577, AP-9): traductor común de errores del front de Diagnóstico. La UI nunca muestra el `message` de un
// ApiError ni de un Error: se traduce por `code` (catálogo de textos fijos de cada ruta) o por `status`, y lo
// desconocido cae en el texto de respaldo del sitio. Solo depende de `@/lib/api`, así no cierra ciclos con los módulos
// de cada ruta, que importan este archivo y definen su catálogo.
import { ApiError } from "@/lib/api";

/** Texto fijo de un 403 que no trae un `code` del catálogo. */
export const TEXTO_SIN_PERMISO = "No tienes permiso para esta acción.";

/**
 * Texto fijo de un error: el del `code` en `porCodigo`, el de permiso en un 403 o, si no, `respaldo`.
 *
 * @example
 * mensajeErrorFijo(new ApiError(422, "…", "SIN_VALIDOS"), "No se pudieron confirmar los vehículos.", ERRORES_CARGA)
 * // → ERRORES_CARGA.SIN_VALIDOS
 */
export function mensajeErrorFijo(error: unknown, respaldo: string, porCodigo: Readonly<Record<string, string>> = {}): string {
  if (error instanceof ApiError) {
    if (error.code && Object.prototype.hasOwnProperty.call(porCodigo, error.code)) return porCodigo[error.code];
    if (error.status === 403) return TEXTO_SIN_PERMISO;
  }
  return respaldo;
}

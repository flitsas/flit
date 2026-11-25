// Traductor común de errores del front de Impuestos. La UI nunca muestra el `message` de un ApiError ni de un Error: se
// traduce por `code` (catálogo de textos fijos de cada pantalla) o por `status`, y lo desconocido cae en el texto de
// respaldo de quien llama. Solo depende de `@/lib/api`, así no cierra ciclos con los módulos de cada ruta.
import { ApiError } from "@/lib/api";

/** Texto fijo de un 403 que no trae un `code` del catálogo. */
export const TEXTO_SIN_PERMISO = "No tienes permiso para esta acción.";

/**
 * Texto fijo de un error: el del `code` en `porCodigo`, el de permiso en un 403 o, si no, `respaldo`.
 *
 * @example
 * mensajeErrorFijo(new ApiError(409, "…", "PRODUCTO_SUSPENDIDO"), "No se pudo liquidar.", ERRORES_LIQUIDACION)
 * // → ERRORES_LIQUIDACION.PRODUCTO_SUSPENDIDO
 */
export function mensajeErrorFijo(error: unknown, respaldo: string, porCodigo: Readonly<Record<string, string>> = {}): string {
  if (error instanceof ApiError) {
    if (error.code && Object.prototype.hasOwnProperty.call(porCodigo, error.code)) return porCodigo[error.code];
    if (error.status === 403) return TEXTO_SIN_PERMISO;
  }
  return respaldo;
}

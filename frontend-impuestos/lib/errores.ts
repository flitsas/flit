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
    if (error.code && Object.prototype.hasOwnProperty.call(porCodigo, error.code)) return conTope(porCodigo[error.code], error.detalle?.tope);
    if (error.status === 403) return TEXTO_SIN_PERMISO;
  }
  return respaldo;
}

/**
 * Sustituye `{tope}` de un literal de x-flit-literales.errores (LIMITE_SUPERADO, TOPE_SUPERA_CONTRACTUAL,
 * TOPE_EXPORTACION) por el `tope` del cuerpo del error. Sin un número, el literal queda tal cual.
 */
export function conTope(literal: string, tope: unknown): string {
  return typeof tope === "number" && Number.isFinite(tope) ? literal.split("{tope}").join(String(tope)) : literal;
}

/** Campo del cuerpo que incumple la regla (`campo` de ErrorRegla), si la API lo nombró. */
export function campoDeError(error: unknown): string | null {
  const campo = error instanceof ApiError ? error.detalle?.campo : undefined;
  return typeof campo === "string" && campo ? campo : null;
}

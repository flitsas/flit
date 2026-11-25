// Traductor común de errores del front de Impuestos. La UI nunca muestra el `message` de un ApiError ni de un Error: se
// traduce por `code` (catálogo de textos fijos de cada pantalla) o por `status`, y lo desconocido cae en el texto de
// respaldo de quien llama. Solo depende de `@/lib/api`, así no cierra ciclos con los módulos de cada ruta.
import { ApiError } from "@/lib/api";

/** Texto fijo de un 403 que no trae un `code` del catálogo. */
export const TEXTO_SIN_PERMISO = "No tienes permiso para esta acción.";

/**
 * x-flit-literales.errores de `?empresa` (contrato 1.1.0, delta 8), común a todas las rutas que la aceptan: quien no es
 * Super Admin y la envía recibe 403 EMPRESA_SOLO_SUPER_ADMIN; el Super Admin con un valor no uuid, repetido o ausente
 * donde opera sobre una sola compañía, 400 EMPRESA_REQUERIDA; una compañía inexistente o sin Impuestos, 404
 * EMPRESA_NO_ENCONTRADA. EMPRESA_NO_PERMITIDA (1.0.0) ya no se emite: se conserva como alias con el mismo literal.
 */
export const ERRORES_EMPRESA = {
  EMPRESA_SOLO_SUPER_ADMIN: "Solo el Super Admin puede elegir la compañía.",
  EMPRESA_NO_PERMITIDA: "Solo el Super Admin puede elegir la compañía.",
  EMPRESA_REQUERIDA: "Elige una compañía.",
  EMPRESA_NO_ENCONTRADA: "La compañía no existe o no tiene Impuestos.",
} as const satisfies Readonly<Record<string, string>>;

/**
 * x-flit-literales.errores transversales (contrato 1.2.2): los puede devolver cualquier ruta, así que no se repiten en
 * los mapas de módulo. mensajeErrorFijo los consulta después del mapa de quien llama y antes del texto de permiso y del
 * respaldo; un mapa de módulo que traiga el mismo código manda sobre este.
 */
export const ERRORES_COMUNES = {
  CONFLICTO_CONCURRENCIA: "Otro usuario cambió este registro al mismo tiempo. Recarga e inténtalo de nuevo.",
  ESCRITURA_ENTRE_EMPRESAS: "No puedes modificar datos de otra compañía.",
  SIN_USUARIO: "Tu sesión no identifica a un usuario. Vuelve a iniciar sesión.",
  VALOR_INVALIDO: "Un valor enviado no es válido. Revisa los datos e inténtalo de nuevo.",
} as const satisfies Readonly<Record<string, string>>;

/** Literal de ERRORES_COMUNES para el `code` del error, o null si no es uno de ellos. */
export function textoErrorComun(error: unknown): string | null {
  if (!(error instanceof ApiError) || !error.code || !Object.prototype.hasOwnProperty.call(ERRORES_COMUNES, error.code)) return null;
  return ERRORES_COMUNES[error.code as keyof typeof ERRORES_COMUNES];
}

/**
 * ¿La API rechazó la `empresa` porque quien la envía no es Super Admin (EMPRESA_SOLO_SUPER_ADMIN o el alias
 * EMPRESA_NO_PERMITIDA)? Es un 403 de la petición, no de acceso a la página: se pinta en línea con su literal.
 */
export function esRechazoDeEmpresa(error: unknown): boolean {
  return error instanceof ApiError && (error.code === "EMPRESA_SOLO_SUPER_ADMIN" || error.code === "EMPRESA_NO_PERMITIDA");
}

/**
 * ¿Es un error de la `empresa` de la petición (cualquiera de ERRORES_EMPRESA: 400 EMPRESA_REQUERIDA, 404
 * EMPRESA_NO_ENCONTRADA, 403 EMPRESA_SOLO_SUPER_ADMIN o su alias)? Se pinta en línea con su literal: no es la 404 del
 * recurso ni la 403 de acceso.
 */
export function esErrorDeEmpresa(error: unknown): boolean {
  return error instanceof ApiError && Boolean(error.code) && Object.prototype.hasOwnProperty.call(ERRORES_EMPRESA, error.code as string);
}

/**
 * Texto fijo de un error: el del `code` en `porCodigo`, si no el de ERRORES_COMUNES, el de permiso en un 403 o, si no,
 * `respaldo`.
 *
 * @example
 * mensajeErrorFijo(new ApiError(409, "…", "PRODUCTO_SUSPENDIDO"), "No se pudo liquidar.", ERRORES_LIQUIDACION)
 * // → ERRORES_LIQUIDACION.PRODUCTO_SUSPENDIDO
 */
export function mensajeErrorFijo(error: unknown, respaldo: string, porCodigo: Readonly<Record<string, string>> = {}): string {
  if (error instanceof ApiError) {
    if (error.code && Object.prototype.hasOwnProperty.call(porCodigo, error.code)) return conTope(porCodigo[error.code], error.detalle?.tope);
    const comun = textoErrorComun(error);
    if (comun) return comun;
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

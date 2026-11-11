import type { EstadoTramite } from './estados';

/**
 * HU #14547 — «Regenerar mandato y consolidado» (solo SuperAdmin). Reglas de UI de la acción, fuera
 * del componente para poder probarlas solas.
 */

/** R4 — los tres estados finales bloquean la acción (espejo de `TramiteEstado.EsFinal` del backend). */
export const ESTADOS_SIN_REGENERAR_MANDATO: readonly EstadoTramite[] = ['aprobado', 'anulado', 'revocado'];

export function permiteRegenerarMandato(estado: string | null | undefined): boolean {
  return !ESTADOS_SIN_REGENERAR_MANDATO.includes(estado as EstadoTramite);
}

const MENSAJE_GENERICO = 'No se pudo regenerar el mandato y el consolidado.';
const MENSAJE_ALMACENAMIENTO =
  'No se pudo guardar en el almacenamiento de archivos. Intenta de nuevo en unos minutos.';

/** Códigos de negocio del endpoint → texto para el SuperAdmin. Nunca se muestra el código crudo. */
const MENSAJES_POR_CODIGO: Record<string, string> = {
  estado_no_permite_regenerar_mandato:
    'Este trámite está aprobado, anulado o revocado: ya no se puede regenerar el mandato.',
  migrado_solo_lectura:
    'Este trámite se migró de la versión anterior y es de solo lectura: no se puede regenerar el mandato.',
  organismo_requerido:
    'El organismo de tránsito del trámite no está seleccionado o no está activo. Selecciónalo antes de regenerar.',
  storage_unavailable: MENSAJE_ALMACENAMIENTO,
};

const MENSAJES_POR_STATUS: Record<number, string> = {
  400: 'No se pudo identificar la compañía del trámite. Recarga la página e inténtalo de nuevo.',
  403: 'Solo un SuperAdmin puede regenerar el mandato y el consolidado.',
  404: 'No se encontró el trámite.',
  503: MENSAJE_ALMACENAMIENTO,
};

/** Un texto con forma de código (`snake_case`) no se le muestra a nadie. */
const PARECE_CODIGO = /[a-z0-9]+_[a-z0-9_]+/i;

/**
 * Traduce el error del `POST …/mandato/regenerar` a un texto de negocio. El código puede venir en
 * `code`, `error`, `title` o `detail` del ProblemDetails (el endpoint de limpiar usa `detail` legible);
 * se buscan todos. Si no hay código conocido, manda el status; un 409 con `detail` legible se muestra
 * tal cual; todo lo demás cae al texto genérico.
 */
export function mensajeErrorRegenerarMandato(err: unknown): string {
  if (!err || typeof err !== 'object') return MENSAJE_GENERICO;
  const { status, problem, message } = err as { status?: unknown; problem?: unknown; message?: unknown };
  if (typeof status !== 'number') return MENSAJE_GENERICO;

  const p = problem && typeof problem === 'object' ? (problem as Record<string, unknown>) : {};
  const candidatos = [p.code, p.error, p.title, p.detail, message].filter(
    (v): v is string => typeof v === 'string',
  );
  for (const codigo of Object.keys(MENSAJES_POR_CODIGO)) {
    if (candidatos.some((c) => c.includes(codigo))) return MENSAJES_POR_CODIGO[codigo];
  }

  const porStatus = MENSAJES_POR_STATUS[status];
  if (porStatus) return porStatus;

  const detalle = typeof p.detail === 'string' ? p.detail.trim() : '';
  if (status === 409 && detalle && !PARECE_CODIGO.test(detalle)) return detalle;
  return MENSAJE_GENERICO;
}

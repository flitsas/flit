import { CAUSAS_FALLO_CONSOLIDADO } from '@/lib/tramites/fallo-regeneracion-consolidado';

/**
 * Épica #12760 (security B2) — traducción de los errores del backend al pedir, abrir o consultar un
 * consolidado. La UI NUNCA pinta el código ni el `detail` crudo del backend (`fur_requerido`,
 * `force_no_permitido_en_get`, trazas, nombres internos): se reconoce el código y se muestra copy
 * amigable; lo desconocido cae en un respaldo genérico por estado HTTP o en el genérico del llamador.
 *
 * Mismo patrón que {@link CAUSAS_FALLO_CONSOLIDADO} / `textoCausaFallo` (#12799), del que reutiliza
 * las causas del generador (`storage_unavailable`, `sin_adjuntos`, …).
 */

/** Copy por código de error de las rutas de generación y entrega del consolidado. */
export const ERRORES_CONSOLIDADO: Readonly<Record<string, string>> = {
  fur_requerido: 'El trámite aún no tiene el FUR generado: genéralo antes de consolidar el expediente.',
  documentos_incompletos: 'Faltan documentos obligatorios del trámite.',
  modalidad_no_soportada: 'Este tipo de trámite no admite expediente consolidado.',
  consolidado_no_generado: 'El trámite aún no tiene consolidado generado.',
  generacion_bloqueada_estado_final:
    'El trámite ya está aprobado o anulado: su documentación es definitiva y no se regenera.',
  // Defecto del cliente (un GET nunca debe forzar): al usuario solo le sirve reintentar.
  force_no_permitido_en_get: 'No se pudo abrir el consolidado. Intenta de nuevo.',
  ...Object.fromEntries(
    Object.entries(CAUSAS_FALLO_CONSOLIDADO).map(([codigo, texto]) => [
      codigo,
      `No se pudo obtener el consolidado: ${texto}.`,
    ]),
  ),
};

export const ERROR_CONSOLIDADO_GENERICO = 'No se pudo obtener el consolidado. Intenta de nuevo.';
export const ERROR_CONSOLIDADO_SIN_PERMISO = 'No tienes permiso para consultar este documento.';
export const ERROR_CONSOLIDADO_SERVICIO =
  'El servicio no está disponible en este momento. Vuelve a intentarlo en unos minutos.';

const CAMPOS_CODIGO = ['code', 'codigo', 'error', 'reason', 'title', 'detail'] as const;

function textosDelError(err: unknown): string[] {
  if (!err || typeof err !== 'object') return typeof err === 'string' ? [err] : [];
  const e = err as { message?: unknown; problem?: unknown; body?: unknown };
  const textos: string[] = [];
  for (const cuerpo of [e.problem, e.body]) {
    if (cuerpo && typeof cuerpo === 'object') {
      for (const campo of CAMPOS_CODIGO) {
        const v = (cuerpo as Record<string, unknown>)[campo];
        if (typeof v === 'string') textos.push(v);
      }
    }
  }
  if (typeof e.message === 'string') textos.push(e.message);
  return textos;
}

/**
 * Código conocido presente en el error (ProblemDetails `code`/`title`/`detail`, cuerpo de `ApiError`
 * o `message`), comparado como palabra completa. `null` si no hay ninguno conocido.
 */
export function codigoErrorConsolidado(err: unknown): string | null {
  const textos = textosDelError(err);
  for (const codigo of Object.keys(ERRORES_CONSOLIDADO)) {
    const patron = new RegExp(`(^|[^a-z_])${codigo}([^a-z_]|$)`);
    if (textos.some((t) => patron.test(t))) return codigo;
  }
  return null;
}

function statusDelError(err: unknown): number | null {
  const s = err && typeof err === 'object' ? (err as { status?: unknown }).status : undefined;
  return typeof s === 'number' ? s : null;
}

/**
 * Mensaje amigable para un error del consolidado. Orden: código conocido → estado HTTP (403 permiso,
 * 404 sin consolidado, 0/5xx servicio) → `respaldo` del llamador. Nunca devuelve el texto crudo.
 *
 * Uso de ejemplo:
 *   mensajeErrorConsolidadoAmigable(new Error('fur_requerido'))
 *   // → 'El trámite aún no tiene el FUR generado: genéralo antes de consolidar el expediente.'
 */
export function mensajeErrorConsolidadoAmigable(
  err: unknown,
  respaldo: string = ERROR_CONSOLIDADO_GENERICO,
): string {
  const codigo = codigoErrorConsolidado(err);
  if (codigo) return ERRORES_CONSOLIDADO[codigo] ?? respaldo;
  const status = statusDelError(err);
  // 403 — p. ej. un gestor que pide `tipo=consolidado_maestro` (solo OT/SuperAdmin). El código exacto
  // de ese rechazo aún no está en el OpenAPI: se resuelve por estado, sin inventar el código.
  if (status === 403) return ERROR_CONSOLIDADO_SIN_PERMISO;
  if (status === 404) return ERRORES_CONSOLIDADO.consolidado_no_generado ?? respaldo;
  if (status === 0 || (status !== null && status >= 500)) return ERROR_CONSOLIDADO_SERVICIO;
  return respaldo;
}

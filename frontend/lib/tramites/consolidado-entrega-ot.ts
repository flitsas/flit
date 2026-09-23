import { ApiError } from '@/lib/api/types';
import type { OtClientProcedure } from '@/lib/api/types-ot';
import type { ConsolidadoEntregaParams } from '@/lib/api/types/procedure-runtime';
import { formatFechaHora } from '@/lib/format/date';

/**
 * HU #12787 — mensaje cuando la ruta de entrega OT del consolidado falla. El 404
 * `consolidado_no_generado` (o trámite sin consolidado) conserva el aviso que la consola ya daba
 * cuando no encontraba el adjunto; el resto es un fallo técnico reintentable.
 */
export function mensajeEntregaOtFallida(e: unknown): string {
  if (e instanceof ApiError && e.status === 404) {
    return 'El trámite aún no tiene consolidado generado.';
  }
  return 'No se pudo abrir el consolidado. Intenta de nuevo.';
}

/**
 * HU #12787 (AC2) — de dónde sale el consolidado MAESTRO en la vista de solo lectura del OT.
 *
 * Decisión del usuario «maestro radicado, fijo»: si el trámite ya se radicó en Quipux, el OT ve el
 * maestro que se radicó tal cual y NUNCA se regenera.
 * - `adjunto_radicado`: radicado y se conoce el adjunto ⇒ se previsualiza/descarga ese adjunto por
 *   la ruta de documentos (`/documents/{id}/preview-url` o `/download`), sin tocar la entrega.
 * - `entrega` con `soloLectura`: radicado sin adjunto conocido ⇒ la entrega sirve el adjunto
 *   existente sin mirar la bandera (no reconstruye).
 * - `entrega` normal: aún no radicado ⇒ flujo de AC1 (la entrega garantiza el vigente).
 */
export type FuenteMaestroOt =
  | { via: 'adjunto_radicado'; attachmentId: string; radicadoEn: string }
  | { via: 'entrega'; params: ConsolidadoEntregaParams; radicadoEn: string | null };

export function resolverFuenteMaestroOt(
  procedure:
    | Pick<OtClientProcedure, 'quipuxRadicadoEn' | 'quipuxMaestroAttachmentId'>
    | null
    | undefined,
): FuenteMaestroOt {
  const radicadoEn = procedure?.quipuxRadicadoEn?.trim() || null;
  if (!radicadoEn) {
    return { via: 'entrega', params: { tipo: 'consolidado_maestro' }, radicadoEn: null };
  }
  const attachmentId = procedure?.quipuxMaestroAttachmentId?.trim() || null;
  if (attachmentId) {
    return { via: 'adjunto_radicado', attachmentId, radicadoEn };
  }
  return {
    via: 'entrega',
    params: { tipo: 'consolidado_maestro', soloLectura: true },
    radicadoEn,
  };
}

/** HU #12787 (AC2) — título del aviso de maestro radicado. */
export const COPY_MAESTRO_RADICADO_TITULO = 'Versión radicada en Quipux';

/**
 * HU #12787 (AC2) — detalle del aviso: fecha de radicación en hora Colombia (formato único del
 * producto, `DD/MM/YYYY HH:mm`) y que el documento no se regenera.
 */
export function textoMaestroRadicado(radicadoEn: string): string {
  return `Radicado el ${formatFechaHora(radicadoEn)} (hora Colombia). Se muestra el documento radicado tal cual; no se regenera.`;
}

/** Campos de vigencia/radicación que aprobar, rechazar y revocar devuelven en `null`. */
const CAMPOS_CONSOLIDADO_OT = [
  'consolidadoWizard',
  'consolidadoMaestro',
  'quipuxRadicadoEn',
  'quipuxMaestroAttachmentId',
] as const;

/**
 * HU #12787 — reconcilia la fila local con la respuesta de una decisión del OT. Las respuestas de
 * aprobar/rechazar/revocar NO recalculan la vigencia ni la radicación (llegan en `null`): si se
 * tomaran tal cual, la fila perdería la fecha de radicación y el siguiente «Ver consolidado»
 * regeneraría un maestro que ya está radicado. Un valor NO nulo de la respuesta sí gana.
 */
export function conservarCamposConsolidadoOt(
  previo: OtClientProcedure,
  actualizado: OtClientProcedure,
): OtClientProcedure {
  const resultado: OtClientProcedure = { ...actualizado };
  for (const campo of CAMPOS_CONSOLIDADO_OT) {
    if (actualizado[campo] == null && previo[campo] != null) {
      // Asignación campo a campo: TS no estrecha la unión de tipos de las cuatro claves.
      (resultado as unknown as Record<string, unknown>)[campo] = previo[campo];
    }
  }
  return resultado;
}

import type { GenerarConsolidadoResult } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12786/#12787 — lectura de la respuesta de la ruta de entrega del consolidado (HU #12785).
 *
 * La UI ya no decide si el PDF está vigente: lo pide a `…/consolidado/entrega` y el backend lo
 * reconstruye solo si la bandera de vigencia está abajo. Lo único que la UI interpreta es si el
 * documento es el DEFINITIVO del trámite, para avisarlo (AC4 de #12786, AC3 de #12787).
 */

/** Copy del aviso de documento final (estado aprobado/anulado/revocado). */
export const COPY_DOCUMENTO_FINAL_TITULO = 'Documento final';
export const COPY_DOCUMENTO_FINAL_DETALLE =
  'El trámite está en un estado final: este es el expediente definitivo y ya no se regenera.';

/**
 * `true` si la entrega sirvió el PDF definitivo del trámite. Se acepta la bandera explícita o
 * cualquiera de los dos modos de estado final: `migrado_solo_lectura` también es estado final
 * (migrado V1), solo que el backend lo nombra distinto.
 */
export function esDocumentoDefinitivo(
  res: Pick<GenerarConsolidadoResult, 'definitivoPorEstadoFinal' | 'modo'> | null | undefined,
): boolean {
  if (!res) return false;
  if (res.definitivoPorEstadoFinal === true) return true;
  return res.modo === 'definitivo_estado_final' || res.modo === 'migrado_solo_lectura';
}

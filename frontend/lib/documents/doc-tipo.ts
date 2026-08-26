import type { ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

/**
 * Emparejamiento entre el `docTipo` de un ítem del checklist y el `tipo` de un adjunto.
 *
 * <p><b>Por qué hace falta una función y no `===`.</b> Los dos extremos no guardan el código igual.
 * El `docTipo` sale de `document_types.code`, que conserva el casing con el que el administrador
 * creó el documento en el módulo Documental (el saneador del formulario solo filtra a
 * `[A-Za-z0-9-]`, no normaliza mayúsculas); el `tipo` del adjunto lo persiste el backend en
 * minúsculas al subir. Comparando tal cual, un tipo con una sola mayúscula en el código se subía
 * —fichero en almacenamiento y fila en la base— y la casilla seguía apareciendo vacía y obligatoria:
 * para el gestor, «no carga».</p>
 *
 * <p>El catálogo no se puede normalizar para evitarlo: conviven a propósito códigos con distinto
 * casing (`SOAT` del seed de organismos y `soat` del catálogo operativo), y unificarlos chocaría
 * contra la unicidad de `code`. Espejo de la comparación del backend en `ChecklistEngine`.</p>
 */
export function mismoDocTipo(a: string | null | undefined, b: string | null | undefined): boolean {
  if (!a || !b) return false;
  return a.toLowerCase() === b.toLowerCase();
}

/** Primer adjunto que corresponde a ese `docTipo`, o `undefined` si aún no se cargó ninguno. */
export function findAttachmentByDocTipo<T extends Pick<ProcedureAttachment, 'tipo'>>(
  attachments: readonly T[],
  docTipo: string | null | undefined,
): T | undefined {
  if (!docTipo) return undefined;
  return attachments.find((a) => mismoDocTipo(a.tipo, docTipo));
}

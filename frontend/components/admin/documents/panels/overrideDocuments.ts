// Helpers compartidos por los tabs de overrides (OT / Cliente).
import { updateDocumentOrderOverride } from "@/lib/api/admin-document-overrides";
import type {
  DocumentOrderOverride,
  DocumentType,
  ProcedureDocumentRequirement,
} from "@/lib/api/types-documents";

// Un override de orden solo es válido para documentos asociados al trámite (el backend
// devuelve 422 en caso contrario), así que el selector se alimenta con esas asociaciones
// y no con el catálogo completo. Mapea cada asociación al shape `DocumentType` que espera
// `DocumentTypeSelect` (que filtra por `estado === "activo"`).
export function toDocumentOptions(
  requirements: ProcedureDocumentRequirement[],
): DocumentType[] {
  return requirements.map((r) => ({
    id: r.documentTypeId,
    codigo: r.documento.codigo,
    nombre: r.documento.nombre,
    estado: r.documento.estado,
    // fechaCreacion no se usa en el selector; el contrato de asociaciones no la expone.
    fechaCreacion: "",
  }));
}

/** Paso de renumeración del arrastre — deja huecos para inserciones manuales futuras. */
export const OVERRIDE_ORDER_STEP = 10;

/** Renumera los overrides a orden secuencial (10, 20, 30…) según su nueva posición. */
export function renumberOverrides(
  ordered: DocumentOrderOverride[],
): DocumentOrderOverride[] {
  return ordered.map((o, index) => ({ ...o, orden: (index + 1) * OVERRIDE_ORDER_STEP }));
}

/**
 * Persiste (PUT) solo los overrides cuyo orden cambió respecto al estado previo, para no
 * disparar peticiones innecesarias. Lanza si alguna falla (el tab revierte el optimista).
 */
export async function persistReorderedOverrides(
  renumbered: DocumentOrderOverride[],
  previousOrderById: Map<string, number>,
): Promise<void> {
  const changed = renumbered.filter((o) => previousOrderById.get(o.id) !== o.orden);
  await Promise.all(changed.map((o) => updateDocumentOrderOverride(o.id, o.orden)));
}

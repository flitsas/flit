// Cliente tipado de la obligatoriedad documental por OT (HU #10198 / backend #10198).
// Granular solo para Organismo de Tránsito. Una función por endpoint del contrato
// (`contracts/openapi/core-api.v1.yaml`).
import { apiFetch } from "./client";
import type {
  DocumentRequirementOverride,
  SetDocumentRequirementOverrideRequest,
} from "./types-documents";

const base = "/api/v1/admin/document-requirement-overrides";

/** GET ?procedureTypeId&transitOfficeId — overrides de obligatoriedad del trámite/OT. */
export async function fetchDocumentRequirementOverrides(
  procedureTypeId: string,
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<DocumentRequirementOverride[]> {
  const result = await apiFetch<{ data: DocumentRequirementOverride[] }>(base, {
    query: { procedureTypeId, transitOfficeId },
    signal,
  });
  return result.data;
}

/**
 * PUT — upsert por tupla natural. `estado` REQUIRED/OPTIONAL/NOT_APPLICABLE crea o
 * actualiza (200, devuelve el override); `estado=DEFAULT` limpia el override (204, devuelve
 * undefined). Lanza ApiValidationError en 422.
 */
export function setDocumentRequirementOverride(
  body: SetDocumentRequirementOverrideRequest,
): Promise<DocumentRequirementOverride | undefined> {
  return apiFetch<DocumentRequirementOverride | undefined>(base, { method: "PUT", body });
}

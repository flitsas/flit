// Cliente tipado del catálogo de tipos de documento (HU #10198 / backend #10193).
// Una función por endpoint del contrato (`contracts/openapi/core-api.v1.yaml`).
import { apiFetch } from "./client";
import type {
  CreateDocumentTypeRequest,
  DocumentType,
  DocumentTypeListParams,
  DocumentTypePagedResult,
  UpdateDocumentTypeRequest,
} from "./types-documents";

const base = "/api/v1/admin/document-types";

/** GET — listado paginado, ordenado por nombre asc (AC1). Solo activos salvo includeInactive. */
export function fetchDocumentTypes(
  params: DocumentTypeListParams = {},
  signal?: AbortSignal,
): Promise<DocumentTypePagedResult> {
  return apiFetch<DocumentTypePagedResult>(base, { query: { ...params }, signal });
}

/** POST — alta de tipo de documento (AC1). Lanza ApiValidationError en 422. */
export function createDocumentType(body: CreateDocumentTypeRequest): Promise<DocumentType> {
  return apiFetch<DocumentType>(base, { method: "POST", body });
}

/** PUT /{id} — edición de tipo de documento (AC1). Lanza ApiValidationError en 422. */
export function updateDocumentType(
  id: string,
  body: UpdateDocumentTypeRequest,
): Promise<DocumentType> {
  return apiFetch<DocumentType>(`${base}/${id}`, { method: "PUT", body });
}

/** DELETE /{id} — baja lógica (soft-delete). Lanza ApiError 409 si está en uso. */
export function deactivateDocumentType(id: string): Promise<void> {
  return apiFetch<void>(`${base}/${id}`, { method: "DELETE" });
}

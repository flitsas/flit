// Cliente tipado de overrides de orden documental y matriz resuelta
// (HU #10198 / backend #10196). Una función por endpoint del contrato
// (`contracts/openapi/core-api.v1.yaml`).
import { apiFetch } from "./client";
import type {
  CreateDocumentOrderOverrideRequest,
  DocumentOrderOverride,
  OverrideScope,
  ResolvedDocumentMatrixRow,
} from "./types-documents";

const base = "/api/v1/admin/document-order-overrides";

export interface OverrideListParams {
  procedureTypeId: string;
  scope: OverrideScope;
  /** Obligatorio si scope=OT. */
  transitOfficeId?: string;
  /** Obligatorio si scope=CLIENTE. */
  clienteId?: string;
}

/** GET — overrides de la combinación (trámite, scope, referencia) ordenados asc (AC3/AC4). */
export async function fetchDocumentOrderOverrides(
  params: OverrideListParams,
  signal?: AbortSignal,
): Promise<DocumentOrderOverride[]> {
  const result = await apiFetch<{ data: DocumentOrderOverride[] }>(base, {
    query: { ...params },
    signal,
  });
  return result.data;
}

/** POST ?scope= — alta de override OT o Cliente (AC3/AC4). Lanza ApiValidationError en 422. */
export function createDocumentOrderOverride(
  scope: OverrideScope,
  body: CreateDocumentOrderOverrideRequest,
): Promise<DocumentOrderOverride> {
  return apiFetch<DocumentOrderOverride>(base, { method: "POST", query: { scope }, body });
}

/** DELETE /{id} — elimina un override; la matriz vuelve al nivel inferior (AC3/AC4). */
export function removeDocumentOrderOverride(id: string): Promise<void> {
  return apiFetch<void>(`${base}/${id}`, { method: "DELETE" });
}

export interface ResolvedMatrixParams {
  procedureTypeId: string;
  transitOfficeId?: string;
  clienteId?: string;
}

/** GET /resolved-document-matrix — orden final con precedencia Cliente > OT > Default (AC5). */
export async function fetchResolvedDocumentMatrix(
  params: ResolvedMatrixParams,
  signal?: AbortSignal,
): Promise<ResolvedDocumentMatrixRow[]> {
  const result = await apiFetch<{ data: ResolvedDocumentMatrixRow[] }>(
    "/api/v1/admin/resolved-document-matrix",
    { query: { ...params }, signal },
  );
  return result.data;
}

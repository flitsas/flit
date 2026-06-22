// Cliente tipado de asociaciones trámite ↔ documento (HU #10198 / backend #10195).
// Una función por endpoint del contrato (`contracts/openapi/core-api.v1.yaml`).
import { apiFetch } from "./client";
import type {
  CreateProcedureDocumentRequirementRequest,
  ProcedureDocumentRequirement,
  UpdateProcedureDocumentRequirementRequest,
} from "./types-documents";

const base = "/api/v1/admin/procedure-document-requirements";

/** GET ?procedureTypeId= — asociaciones del trámite ordenadas por orden default (AC2). */
export async function fetchProcedureDocumentRequirements(
  procedureTypeId: string,
  signal?: AbortSignal,
): Promise<ProcedureDocumentRequirement[]> {
  const result = await apiFetch<{ data: ProcedureDocumentRequirement[] }>(base, {
    query: { procedureTypeId },
    signal,
  });
  return result.data;
}

/** POST — asocia un documento al trámite (AC2). Lanza ApiValidationError en 422. */
export function createProcedureDocumentRequirement(
  body: CreateProcedureDocumentRequirementRequest,
): Promise<ProcedureDocumentRequirement> {
  return apiFetch<ProcedureDocumentRequirement>(base, { method: "POST", body });
}

/** PUT /{id} — actualiza orden default y obligatoriedad (AC2 reordenamiento/toggle). */
export function updateProcedureDocumentRequirement(
  id: string,
  body: UpdateProcedureDocumentRequirementRequest,
): Promise<ProcedureDocumentRequirement> {
  return apiFetch<ProcedureDocumentRequirement>(`${base}/${id}`, { method: "PUT", body });
}

/** DELETE /{id} — elimina la asociación (AC2). Lanza ApiError 409 si está en uso. */
export function removeProcedureDocumentRequirement(id: string): Promise<void> {
  return apiFetch<void>(`${base}/${id}`, { method: "DELETE" });
}

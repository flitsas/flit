// Cliente tipado de los parámetros documentales por compañía gestora (HU #10523 / backend #10521).
// Consume /api/v1/admin/companies/{tenantId}/document-params (GET lista, PUT upsert).
import { apiFetch } from "./client";

export type CompanyDocumentParamState = "OCULTO" | "OBLIGATORIO" | "OPCIONAL";

export interface CompanyDocumentParam {
  id: string;
  documentTypeCode: string;
  state: CompanyDocumentParamState;
}

export interface UpsertCompanyDocumentParamBody {
  documentTypeCode: string;
  state: CompanyDocumentParamState;
}

const base = (tenantId: string) => `/api/v1/admin/companies/${tenantId}/document-params`;

/** GET — parámetros documentales de la gestora, ordenados por código de documento. */
export function fetchCompanyDocumentParams(
  tenantId: string,
  signal?: AbortSignal,
): Promise<CompanyDocumentParam[]> {
  return apiFetch<CompanyDocumentParam[]>(base(tenantId), { signal });
}

/** PUT — crea o actualiza (upsert) el estado documental de un tipo para la gestora. */
export function upsertCompanyDocumentParam(
  tenantId: string,
  body: UpsertCompanyDocumentParamBody,
): Promise<CompanyDocumentParam> {
  return apiFetch<CompanyDocumentParam>(base(tenantId), { method: "PUT", body });
}

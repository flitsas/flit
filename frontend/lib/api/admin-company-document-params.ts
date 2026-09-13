// Cliente tipado de los parámetros documentales por compañía gestora (HU #10523 / backend #10521).
// Consume /api/v1/admin/companies/{tenantId}/document-params (GET lista, PUT upsert).
import { apiFetch } from "./client";
import { companyScopedPath } from "./company-scoped-path";

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

const base = (tenantId: string, networkHeadId?: string | null) =>
  companyScopedPath(tenantId, "/document-params", networkHeadId);

/** GET — parámetros documentales de la gestora, ordenados por código de documento. */
export function fetchCompanyDocumentParams(
  tenantId: string,
  signal?: AbortSignal,
  networkHeadId?: string | null,
): Promise<CompanyDocumentParam[]> {
  return apiFetch<CompanyDocumentParam[]>(base(tenantId, networkHeadId), { signal });
}

/** PUT — crea o actualiza (upsert) el estado documental de un tipo para la gestora. */
export function upsertCompanyDocumentParam(
  tenantId: string,
  body: UpsertCompanyDocumentParamBody,
  networkHeadId?: string | null,
): Promise<CompanyDocumentParam> {
  return apiFetch<CompanyDocumentParam>(base(tenantId, networkHeadId), { method: "PUT", body });
}

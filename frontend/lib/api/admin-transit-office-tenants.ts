// Cliente tipado de alta/listado de tenants OT (refactor adminOT). Exclusivo
// SuperAdmin — a diferencia de `admin-ot.ts` (self-service, OtModulePolicy).
import { apiFetch } from "./client";

export type TransitOfficeTenantOperationMode = "dashboard" | "quipux";

export interface TransitOfficeTenantItem {
  id: string;
  legalName: string;
  taxId: string;
  code: string;
  tenantType: "RENTING";
  estadoActivo: boolean;
  fechaCreacion: string;
  rowVersion: number;
  transitOfficeId: string;
  transitOfficeName: string;
  transitOfficeCode: string;
  operationMode: TransitOfficeTenantOperationMode;
}

export interface TransitOfficeTenantPagedResult {
  data: TransitOfficeTenantItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface TransitOfficeTenantsIndexParams {
  legalName?: string;
  estadoActivo?: boolean;
  page?: number;
  pageSize?: number;
}

export interface CreateTransitOfficeRequest {
  transitOfficeId: string;
  legalName: string;
  taxId: string;
  code: string;
  operationMode?: TransitOfficeTenantOperationMode;
}

const base = "/api/v1/admin/transit-office-tenants";

/** GET /index — listado paginado de tenants OT. */
export function fetchTransitOfficeTenants(
  params: TransitOfficeTenantsIndexParams = {},
  signal?: AbortSignal,
): Promise<TransitOfficeTenantPagedResult> {
  return apiFetch<TransitOfficeTenantPagedResult>(`${base}/index`, { query: { ...params }, signal });
}

/** POST — alta de tenant OT. Lanza ApiValidationError en 422. */
export function createTransitOfficeTenant(
  body: CreateTransitOfficeRequest,
): Promise<TransitOfficeTenantItem> {
  return apiFetch<TransitOfficeTenantItem>(base, { method: "POST", body });
}

/** PUT /{tenantId}/status — activa/desactiva el tenant OT. */
export function setTransitOfficeTenantStatus(
  tenantId: string,
  estadoActivo: boolean,
): Promise<{ id: string; estadoActivo: boolean }> {
  return apiFetch<{ id: string; estadoActivo: boolean }>(`${base}/${tenantId}/status`, {
    method: "PUT",
    body: { estadoActivo },
  });
}

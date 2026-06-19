// Cliente tipado de la API admin de compañías (HU #10194). Una función por
// endpoint del contrato (`contracts/openapi/core-api.v1.yaml`).
import { apiFetch } from "./client";
import type {
  AuditLogPageResponse,
  CompaniesIndexParams,
  CompanyPagedResult,
  TenantSettings,
  TenantSettingsUpdate,
  TransitGrantsResponse,
  TransitOffice,
  WhitelistAddResponse,
  WhitelistEntry,
} from "./types";

const base = "/api/v1/admin/companies";

/** GET /index — listado paginado con filtros server-side (AC1). */
export function fetchCompaniesIndex(
  params: CompaniesIndexParams = {},
  signal?: AbortSignal,
): Promise<CompanyPagedResult> {
  return apiFetch<CompanyPagedResult>(`${base}/index`, { query: { ...params }, signal });
}

/** GET /{tenantId}/settings — configuración operativa actual (AC2). */
export function fetchTenantSettings(tenantId: string, signal?: AbortSignal): Promise<TenantSettings> {
  return apiFetch<TenantSettings>(`${base}/${tenantId}/settings`, { signal });
}

/** PUT /{tenantId}/settings — guardado atómico (AC2). Lanza ApiValidationError en 422. */
export function updateTenantSettings(
  tenantId: string,
  body: TenantSettingsUpdate,
): Promise<TenantSettings> {
  return apiFetch<TenantSettings>(`${base}/${tenantId}/settings`, { method: "PUT", body });
}

/** GET /{tenantId}/whitelist — correos exentos (AC3). */
export function fetchWhitelist(tenantId: string, signal?: AbortSignal): Promise<WhitelistEntry[]> {
  return apiFetch<WhitelistEntry[]>(`${base}/${tenantId}/whitelist`, { signal });
}

/** POST /{tenantId}/whitelist — alta masiva (AC3). Lanza ApiValidationError en 422. */
export function addWhitelistEmails(
  tenantId: string,
  emails: string[],
  reason?: string,
): Promise<WhitelistAddResponse> {
  return apiFetch<WhitelistAddResponse>(`${base}/${tenantId}/whitelist`, {
    method: "POST",
    body: { emails, reason },
  });
}

/** GET /api/v1/admin/transit-offices — catálogo OT, con búsqueda opcional (AC4). */
export function fetchTransitOffices(search?: string, signal?: AbortSignal): Promise<TransitOffice[]> {
  return apiFetch<TransitOffice[]>("/api/v1/admin/transit-offices", { query: { search }, signal });
}

/** GET /{tenantId}/transit-grants — ids de OT habilitados (AC4). */
export function fetchTransitGrants(tenantId: string, signal?: AbortSignal): Promise<TransitGrantsResponse> {
  return apiFetch<TransitGrantsResponse>(`${base}/${tenantId}/transit-grants`, { signal });
}

/** POST /{tenantId}/transit-grants — habilita un OT (AC4, idempotente). */
export function addTransitGrant(tenantId: string, transitOfficeId: string): Promise<void> {
  return apiFetch<void>(`${base}/${tenantId}/transit-grants`, {
    method: "POST",
    body: { transitOfficeId },
  });
}

/** DELETE /{tenantId}/transit-grants/{transitOfficeId} — deshabilita un OT (AC4). */
export function removeTransitGrant(tenantId: string, transitOfficeId: string): Promise<void> {
  return apiFetch<void>(`${base}/${tenantId}/transit-grants/${transitOfficeId}`, {
    method: "DELETE",
  });
}

/** GET /{tenantId}/audit-log — historial paginado DESC (AC5). */
export function fetchAuditLog(
  tenantId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal,
): Promise<AuditLogPageResponse> {
  return apiFetch<AuditLogPageResponse>(`${base}/${tenantId}/audit-log`, {
    query: { page, pageSize },
    signal,
  });
}

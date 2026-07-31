// Cliente tipado de la API admin de compañías (HU #10194). Una función por
// endpoint del contrato (`contracts/openapi/core-api.v1.yaml`).
import { apiFetch } from "./client";
import { ApiError } from "./types";
import type {
  AuditLogPageResponse,
  BlockingCriterion,
  CompaniesIndexParams,
  CompanyListItem,
  CompanyPagedResult,
  ConsultationRestrictionKind,
  CreateCompanyRequest,
  OtBlockingPolicy,
  OtConsultationRestriction,
  TenantSettings,
  TenantSettingsUpdate,
  TransitGrantsResponse,
  TransitOffice,
  UpdateCompanyRequest,
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

/**
 * GET /{tenantId} — identidad de la compañía (HU #11062), para rotular la consola de configuración.
 * Devuelve `null` si no existe: la pantalla sigue usable sin el encabezado en vez de romperse.
 */
export async function fetchCompany(
  tenantId: string,
  signal?: AbortSignal,
): Promise<CompanyListItem | null> {
  try {
    return await apiFetch<CompanyListItem>(`${base}/${tenantId}`, { signal });
  } catch {
    return null;
  }
}

/** POST /api/v1/admin/companies — alta de compañía. Lanza ApiValidationError en 422. */
export function createCompany(body: CreateCompanyRequest): Promise<CompanyListItem> {
  return apiFetch<CompanyListItem>(base, { method: "POST", body });
}

/**
 * PUT /{tenantId} — edición de compañía (#10118). Actualiza razón social, NIT, tipo y
 * estado (el código es inmutable). Lanza ApiValidationError en 422 y ApiError 404 si el
 * tenant no existe.
 */
export function updateCompany(
  tenantId: string,
  body: UpdateCompanyRequest,
): Promise<CompanyListItem> {
  return apiFetch<CompanyListItem>(`${base}/${tenantId}`, { method: "PUT", body });
}

/**
 * PUT /{tenantId}/status — activa/desactiva la compañía (#10118). Devuelve la compañía
 * con su estado actualizado. Lanza ApiError 404 si el tenant no existe.
 */
export function setCompanyStatus(tenantId: string, estadoActivo: boolean): Promise<CompanyListItem> {
  return apiFetch<CompanyListItem>(`${base}/${tenantId}/status`, {
    method: "PUT",
    body: { estadoActivo },
  });
}

/**
 * GET /{tenantId}/settings — configuración operativa actual (AC2). Devuelve `null`
 * en 404 (compañía aún sin configurar): el caller muestra el formulario en blanco.
 */
export async function fetchTenantSettings(
  tenantId: string,
  signal?: AbortSignal,
): Promise<TenantSettings | null> {
  try {
    return await apiFetch<TenantSettings>(`${base}/${tenantId}/settings`, { signal });
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }
    throw error;
  }
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

/**
 * GET /{tenantId}/ot-consultation-restrictions — restricciones de consulta por OT
 * (HU #10759 AC1/AC5). Tabla dispersa: solo vuelven los pares configurados
 * explícitamente; la ausencia de fila equivale a consulta permitida.
 */
export function fetchOtConsultationRestrictions(
  tenantId: string,
  signal?: AbortSignal,
): Promise<OtConsultationRestriction[]> {
  return apiFetch<OtConsultationRestriction[]>(`${base}/${tenantId}/ot-consultation-restrictions`, {
    signal,
  });
}

/**
 * PUT /{tenantId}/ot-consultation-restrictions/{transitOfficeId}/{kind} — fija el estado
 * deseado de una consulta (HU #10759 AC1–AC4). Idempotente en ambos sentidos (sin 404).
 * Lanza ApiValidationError en 422 si el OT no está habilitado para la compañía.
 */
export function setOtConsultationRestriction(
  tenantId: string,
  transitOfficeId: string,
  kind: ConsultationRestrictionKind,
  enabled: boolean,
): Promise<void> {
  return apiFetch<void>(
    `${base}/${tenantId}/ot-consultation-restrictions/${transitOfficeId}/${kind}`,
    { method: "PUT", body: { enabled } },
  );
}

/**
 * GET /{tenantId}/ot-blocking-policies — políticas de bloqueo de preflight por OT (FEATURE 05).
 * Tabla dispersa: solo vuelven los pares configurados explícitamente; la ausencia de fila
 * equivale al default del criterio.
 */
export function fetchOtBlockingPolicies(
  tenantId: string,
  signal?: AbortSignal,
): Promise<OtBlockingPolicy[]> {
  return apiFetch<OtBlockingPolicy[]>(`${base}/${tenantId}/ot-blocking-policies`, {
    signal,
  });
}

/**
 * PUT /{tenantId}/ot-blocking-policies/{transitOfficeId}/{criterion} — fija si un criterio del
 * preflight bloquea (true) o solo advierte (false) para un OT (FEATURE 05). Idempotente en ambos
 * sentidos. Lanza ApiValidationError en 422 si el OT no está habilitado para la compañía.
 */
export function setOtBlockingPolicy(
  tenantId: string,
  transitOfficeId: string,
  criterion: BlockingCriterion,
  blocks: boolean,
): Promise<void> {
  return apiFetch<void>(
    `${base}/${tenantId}/ot-blocking-policies/${transitOfficeId}/${criterion}`,
    { method: "PUT", body: { blocks } },
  );
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

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

/**
 * Estado operativo de un OT del catálogo (RF01). Una fila por oficina;
 * `tenantId`/`estadoActivo`/`operationMode` son `null` cuando no tiene tenant (sin alta).
 *
 * Ojo con los dos «Quipux», que son conceptos DISTINTOS:
 * - `operationMode === "quipux"` describe al OT **cliente** de FLIT: su consola queda en
 *   solo lectura porque aprueba/rechaza dentro de Quipux. Solo 12 de 317 tienen perfil.
 * - `divipoCode` + `quipux*` describen a la secretaría **destino** a la que FLIT radica
 *   trámites vía Quipux (HU #10710). Existen para las 317, sean o no clientes de FLIT.
 */
export interface TransitOfficeOperationalStatus {
  id: string;
  code: string;
  name: string;
  departmentCode: string;
  hasTenant: boolean;
  tenantId: string | null;
  estadoActivo: boolean | null;
  operationMode: TransitOfficeTenantOperationMode | null;
  /**
   * Código DIVIPO que espera Quipux. `null` = aún no se conoce; es el estado normal (hoy
   * solo 6 de 317 están cargadas), NO un error. Sin él la secretaría no es elegible para
   * radicar, aunque tenga banderas activas.
   */
  divipoCode: string | null;
  /** ¿Se radican las matrículas de esta secretaría por Quipux? */
  quipuxRegistration: boolean;
  /** ¿Se radican los traspasos de esta secretaría por Quipux? */
  quipuxTransfer: boolean;
  /** ¿Se radican los otros trámites de esta secretaría por Quipux? */
  quipuxOther: boolean;
}

/** Estado destino de la parametrización Quipux de una secretaría (PUT completo). */
export interface UpdateTransitOfficeQuipuxSettingsRequest {
  /** `null` o vacío = «aún no se conoce». No es un error. */
  divipoCode: string | null;
  quipuxRegistration: boolean;
  quipuxTransfer: boolean;
  quipuxOther: boolean;
}

/** Parametrización Quipux persistida, con la elegibilidad ya calculada por el backend. */
export interface TransitOfficeQuipuxSettings {
  transitOfficeId: string;
  divipoCode: string | null;
  quipuxRegistration: boolean;
  quipuxTransfer: boolean;
  quipuxOther: boolean;
  /** `true` solo con DIVIPO **y** alguna bandera activa. Sin DIVIPO nunca se radica. */
  elegible: boolean;
}

/**
 * ¿Está la secretaría lista para radicar por Quipux? Misma regla que el backend
 * (`elegible`), replicada para pintar el listado sin una llamada por fila.
 */
export function isQuipuxElegible(office: {
  divipoCode: string | null;
  quipuxRegistration: boolean;
  quipuxTransfer: boolean;
  quipuxOther: boolean;
}): boolean {
  return (
    Boolean(office.divipoCode) &&
    (office.quipuxRegistration || office.quipuxTransfer || office.quipuxOther)
  );
}

/** ¿Hay banderas activas pero sin DIVIPO? Estado inconsistente: se declara pero no se radica. */
export function hasQuipuxFlagsWithoutDivipo(office: {
  divipoCode: string | null;
  quipuxRegistration: boolean;
  quipuxTransfer: boolean;
  quipuxOther: boolean;
}): boolean {
  return (
    !office.divipoCode &&
    (office.quipuxRegistration || office.quipuxTransfer || office.quipuxOther)
  );
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

/**
 * GET /api/v1/admin/transit-offices/operational-status — estado operativo por OT
 * (RF01, SuperAdmin). Por cada oficina del catálogo indica si tiene tenant y su estado.
 */
export function fetchTransitOfficesOperationalStatus(
  signal?: AbortSignal,
): Promise<TransitOfficeOperationalStatus[]> {
  return apiFetch<TransitOfficeOperationalStatus[]>(
    "/api/v1/admin/transit-offices/operational-status",
    { signal },
  );
}

/** POST — alta de tenant OT. Lanza ApiValidationError en 422. */
export function createTransitOfficeTenant(
  body: CreateTransitOfficeRequest,
): Promise<TransitOfficeTenantItem> {
  return apiFetch<TransitOfficeTenantItem>(base, { method: "POST", body });
}

/**
 * PUT /api/v1/admin/transit-offices/{id}/quipux-settings — parametriza la radicación
 * Quipux de una secretaría del catálogo (HU #10710, SuperAdmin). Reemplazo completo.
 * Lanza ApiError con `code` DIVIPO_CODE_INVALID en 422.
 */
export function updateTransitOfficeQuipuxSettings(
  transitOfficeId: string,
  body: UpdateTransitOfficeQuipuxSettingsRequest,
): Promise<TransitOfficeQuipuxSettings> {
  return apiFetch<TransitOfficeQuipuxSettings>(
    `/api/v1/admin/transit-offices/${transitOfficeId}/quipux-settings`,
    { method: "PUT", body },
  );
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

// ── Cola Quipux de la secretaría destino (HU #10774) ────────────────────────────────────
// Radicaciones (tramites.quipux_submissions) de los trámites cuyo transit_office_id es esta
// secretaría. NO es la bandeja del OT-cliente (admin-ot.ts): son las radicaciones a la
// secretaría destino, con su estado en el ciclo de Quipux.

/** Estado de una radicación en el ciclo de Quipux. */
export type QuipuxSubmissionStatus =
  | "pendiente"
  | "registrado"
  | "aprobado"
  | "rechazado"
  | "fallido";

/** Una fila de la cola Quipux: la submission + datos del trámite para hacerla legible. */
export interface QuipuxColaItem {
  id: string;
  procedureInstanceId: string;
  referenceNumber: string;
  procedureTypeName: string;
  clientTenantName: string;
  documentName: string;
  status: QuipuxSubmissionStatus;
  attempts: number;
  pollCount: number;
  qxRegisterCode: number | null;
  qxProcedureCode: number | null;
  rejectionReason: string | null;
  createdAt: string;
  registeredAt: string | null;
  lastPolledAt: string | null;
  completedAt: string | null;
  updatedAt: string | null;
}

export interface QuipuxColaPage {
  data: QuipuxColaItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface QuipuxColaParams {
  page?: number;
  pageSize?: number;
}

/** Estados activos (en vuelo hacia Quipux) vs. cerrados (histórico). */
export const QUIPUX_ACTIVE_STATUSES: readonly QuipuxSubmissionStatus[] = ["pendiente", "registrado"];

/** ¿Se puede re-encolar? Solo un dead-letter (`fallido`). */
export function canRetryQuipuxSubmission(item: Pick<QuipuxColaItem, "status">): boolean {
  return item.status === "fallido";
}

/** ¿Se puede cancelar? Solo mientras aún no se ha radicado (`pendiente`). */
export function canCancelQuipuxSubmission(item: Pick<QuipuxColaItem, "status">): boolean {
  return item.status === "pendiente";
}

/**
 * GET /api/v1/admin/transit-offices/{id}/quipux-submissions — cola (activa + histórico) de
 * la secretaría destino, paginada. SuperAdmin.
 */
export function fetchQuipuxCola(
  transitOfficeId: string,
  params: QuipuxColaParams = {},
  signal?: AbortSignal,
): Promise<QuipuxColaPage> {
  return apiFetch<QuipuxColaPage>(
    `/api/v1/admin/transit-offices/${transitOfficeId}/quipux-submissions`,
    { query: { ...params }, signal },
  );
}

/**
 * POST .../{submissionId}/retry — re-encola una radicación `fallido`. Lanza ApiError con
 * `code` QUIPUX_SUBMISSION_INVALID_STATE / _CONFLICT / _NOT_FOUND en 409/404.
 */
export function retryQuipuxSubmission(
  transitOfficeId: string,
  submissionId: string,
): Promise<{ code: string }> {
  return apiFetch<{ code: string }>(
    `/api/v1/admin/transit-offices/${transitOfficeId}/quipux-submissions/${submissionId}/retry`,
    { method: "POST" },
  );
}

/** POST .../{submissionId}/cancel — cancela una radicación `pendiente`. */
export function cancelQuipuxSubmission(
  transitOfficeId: string,
  submissionId: string,
): Promise<{ code: string }> {
  return apiFetch<{ code: string }>(
    `/api/v1/admin/transit-offices/${transitOfficeId}/quipux-submissions/${submissionId}/cancel`,
    { method: "POST" },
  );
}

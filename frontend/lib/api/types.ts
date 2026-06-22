// Tipos del contrato API admin de compañías (HU #10194). Alineados manualmente con
// `contracts/openapi/core-api.v1.yaml` (sin codegen). Serialización camelCase.

// ── Listado de compañías (AC1) ──────────────────────────────────────────────
export interface CompanyListItem {
  id: string;
  nit: string;
  razonSocial: string;
  estadoActivo: boolean;
  fechaCreacion: string;
}

export interface CompanyPagedResult {
  data: CompanyListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CompaniesIndexParams {
  nit?: string;
  razonSocial?: string;
  estadoActivo?: boolean;
  fechaDesde?: string;
  fechaHasta?: string;
  page?: number;
  pageSize?: number;
}

// ── Alta de compañía ────────────────────────────────────────────────────────
export type TenantType = "RENTING" | "CONCESIONARIO" | "FLIT";

/** Etiquetas legibles para el tipo de compañía (el valor enviado sigue siendo el enum). */
export const TENANT_TYPE_LABELS: Record<TenantType, string> = {
  RENTING: "Renting",
  CONCESIONARIO: "Concesionario",
  FLIT: "FLIT",
};

/** Payload del POST /api/v1/admin/companies (alta de compañía). */
export interface CreateCompanyRequest {
  razonSocial: string;
  nit: string;
  code: string;
  tenantType: TenantType;
  estadoActivo: boolean;
}

// ── Configuración del tenant (AC2) ──────────────────────────────────────────
export interface SwitchesMatricula {
  allowInitialRegistration: boolean;
  allowMiscNewVehicles: boolean;
  onlyOwnVehicles: boolean;
}

export type EnrutamientoSMTP = "FLIT_SMTP" | "TENANT_API";
export type NotificationTarget = "COMPRADOR" | "RADICADOR" | "NINGUNO";

export interface TenantSettings {
  tenantId: string;
  switchesMatricula: SwitchesMatricula;
  baulFirmasActivo: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget: NotificationTarget;
  metodosRecaudo: string[];
}

/** Payload del PUT settings — los mismos campos editables (sin tenantId). */
export interface TenantSettingsUpdate {
  switchesMatricula: SwitchesMatricula;
  baulFirmasActivo: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget: NotificationTarget;
  metodosRecaudo: string[];
}

// ── Errores de validación 422 ───────────────────────────────────────────────
export interface ValidationError {
  field: string;
  message: string;
  value?: string | null;
}

export interface ValidationErrorResponse {
  errors: ValidationError[];
}

// ── Whitelist (AC3) ─────────────────────────────────────────────────────────
export interface WhitelistEntry {
  email: string;
  createdAt: string;
  addedBy?: string | null;
}

export interface WhitelistAddResponse {
  added: string[];
  skipped: string[];
}

// ── Organismos de tránsito / grants (AC4) ───────────────────────────────────
export interface TransitOffice {
  id: string;
  code: string;
  name: string;
  departmentCode: string;
  cityCode: string;
}

export interface TransitGrantsResponse {
  transitOfficeIds: string[];
}

// ── Audit log (AC5) ─────────────────────────────────────────────────────────
export interface AuditLogEntry {
  entityName: string;
  fieldName: string;
  oldValue?: string | null;
  newValue?: string | null;
  changedBy?: string | null;
  changedAt: string;
}

export interface AuditLogPageResponse {
  data: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Error de validación tipado para flujos 422 (AC2/AC3). */
export class ApiValidationError extends Error {
  constructor(
    public readonly errors: ValidationError[],
    public readonly status: number,
  ) {
    super("Validación fallida");
    this.name = "ApiValidationError";
  }
}

/** Error HTTP genérico que conserva el status para distinguir casos (p. ej. 404). */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

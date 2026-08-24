// Tipos del contrato API admin de compañías (HU #10194). Alineados manualmente con
// `contracts/openapi/core-api.v1.yaml` (sin codegen). Serialización camelCase.

// ── Listado de compañías (AC1) ──────────────────────────────────────────────
export interface CompanyListItem {
  id: string;
  nit: string;
  razonSocial: string;
  /** Código único del tenant (inmutable; identificador de sistema). */
  code: string;
  /**
   * Tipo de compañía. `string` (no el enum B2B) porque el listado incluye tenants con
   * tipos heredados fuera del catálogo (p.ej. `standard`, `transit_office`).
   */
  tenantType: string;
  /**
   * `true` si el tenant es Organismo de Tránsito (perfil en `admin.transit_office_profiles`).
   * No inferir solo desde `tenantType`: los OT también usan RENTING.
   */
  isTransitOffice: boolean;
  estadoActivo: boolean;
  fechaCreacion: string;
  /**
   * Token de concurrencia optimista (`identity.tenants.row_version`). Se reenvía al editar
   * para que el backend detecte ediciones concurrentes (409 si otra persona la modificó).
   */
  rowVersion: number;
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
  /** Default API true: excluye Organismos de Tránsito del listado B2B. */
  excludeTransitOffices?: boolean;
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

/** Catálogo B2B administrable desde la consola de compañías. */
export const B2B_TENANT_TYPES: readonly TenantType[] = ["RENTING", "CONCESIONARIO", "FLIT"];

/**
 * `true` si el tipo pertenece al catálogo B2B editable. Los tenants con tipos heredados
 * de sistema (`standard`, `transit_office`, …) aparecen en el listado pero NO se editan
 * desde esta consola (decisión de producto): se muestran como solo-lectura.
 */
export function isB2BTenantType(value: string): value is TenantType {
  return (B2B_TENANT_TYPES as readonly string[]).includes(value);
}

/** Payload del POST /api/v1/admin/companies (alta de compañía). */
export interface CreateCompanyRequest {
  razonSocial: string;
  nit: string;
  code: string;
  tenantType: TenantType;
  estadoActivo: boolean;
}

/**
 * Payload del PUT /api/v1/admin/companies/{tenantId} (edición de compañía). El
 * código es inmutable, por lo que no se envía. `tenantType` es `string` (no el enum
 * B2B) porque el listado incluye tenants con tipos heredados fuera del catálogo
 * (p.ej. `standard`, `transit_office`); al editar se preserva su valor actual.
 */
export interface UpdateCompanyRequest {
  razonSocial: string;
  nit: string;
  tenantType: string;
  estadoActivo: boolean;
  /** Versión leída al abrir la edición; el backend responde 409 si quedó desactualizada. */
  rowVersion: number;
}

// ── Configuración del tenant (AC2) ──────────────────────────────────────────
export interface SwitchesMatricula {
  allowInitialRegistration: boolean;
  allowMiscNewVehicles: boolean;
  /** Legado: espejo de `onlyOwnVehiclesByFamily.traspaso`. */
  onlyOwnVehicles: boolean;
  /** Solo vehículos propios por familia de trámite. */
  onlyOwnVehiclesByFamily?: OnlyOwnVehiclesByFamily;
  /**
   * Bloqueo de creación por familia. `true` = no permitir crear trámites de esa familia.
   * Matrículas = invertido de `allowInitialRegistration`.
   */
  blockProcedureFamily?: BlockProcedureFamily;
}

/** Flags por familia (`MATRICULAS` | `TRASPASO` | `OTROS`). */
export interface OnlyOwnVehiclesByFamily {
  matriculas: boolean;
  traspaso: boolean;
  otros: boolean;
}

/** Bloqueo de creación por familia (`true` = no crear). */
export interface BlockProcedureFamily {
  matriculas: boolean;
  traspaso: boolean;
  otros: boolean;
}

export type EnrutamientoSMTP = "FLIT_SMTP" | "TENANT_API";
export type NotificationTarget = "COMPRADOR" | "RADICADOR" | "NINGUNO";

export interface DestinatariosNotificacion {
  comprador: boolean;
  vendedorOPropietario: boolean;
  radicador: boolean;
  extraEmail: string | null;
}

/**
 * Fuente de la consulta de comparendos de la compañía (FEATURE 02): `internal` (módulo de
 * comparendos con fuente base) o `external` (consulta en línea al SIMIT). Su USO en el flujo
 * del trámite llega en FEATURE 05.
 */
export type FinesQuerySource = "internal" | "external";

/** Proveedor primario + orden de fallback para un tipo de consulta RUNT (HU #10478). */
export interface ConsultationProviderChoice {
  primary: string;
  fallback: string[];
}

/**
 * Override por tenant de la cadena de proveedores de consulta RUNT (HU #10478). Claves:
 * `vehicle_vin`, `vehicle_plate`, `conductor`. Objeto vacío = usar los defaults globales.
 */
export type ConsultationProviderConfig = Record<string, ConsultationProviderChoice>;

/**
 * Proveedores de avalúo habilitados por tenant (Feature #10707). `primary` es el sugerido por
 * defecto; `enabled` incluye siempre a `fasecolda` (proveedor base). Los demás (`base_gravable`,
 * `mercado_libre`) se habilitan por compañía.
 */
export interface AvaluoProviderConfig {
  primary: string;
  enabled: string[];
}

export interface TenantSettings {
  tenantId: string;
  switchesMatricula: SwitchesMatricula;
  baulFirmasActivo: boolean;
  /** Preasignación de placa activa (Feature #10587, matrícula inicial). */
  preasignacionPlacaActiva: boolean;
  /**
   * Con placa completa/rango al radicar → Terminado directo (omite paso Asignado del gestor).
   * Default false.
   */
  plateFlowSkipToTerminado?: boolean;
  /** Opción activa: permite continuar aunque el RUNT no reporte SOAT vigente. Apagada: bloquea. */
  validarSoatConRunt?: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget?: NotificationTarget;
  avisosAprobacionActivos?: boolean;
  avisosRechazoActivos?: boolean;
  destinatariosNotificacion?: DestinatariosNotificacion;
  metodosRecaudo: string[];
  // HU #10478 — opcionales en el tipo por compatibilidad; el backend siempre los devuelve.
  runtFailoverTimeoutMs?: number;
  consultationProviderConfig?: ConsultationProviderConfig;
  // Feature #10707 — proveedores de avalúo (el backend siempre lo devuelve).
  avaluoProviderConfig?: AvaluoProviderConfig;
  // FEATURE 02 — opcional por compatibilidad; el backend siempre lo devuelve.
  finesQuerySource?: FinesQuerySource;
  /** HU #11469 legado. */
  avisosCambioEstadoActivos?: boolean;
}

/** Payload del PUT settings — los mismos campos editables (sin tenantId). */
export interface TenantSettingsUpdate {
  switchesMatricula: SwitchesMatricula;
  baulFirmasActivo: boolean;
  /** Preasignación de placa activa (Feature #10587, matrícula inicial). */
  preasignacionPlacaActiva: boolean;
  /**
   * Con placa completa/rango al radicar → Terminado directo (omite paso Asignado del gestor).
   * Default false.
   */
  plateFlowSkipToTerminado?: boolean;
  /** Opción activa: permite continuar aunque el RUNT no reporte SOAT vigente. Apagada: bloquea. */
  validarSoatConRunt?: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget?: NotificationTarget;
  avisosAprobacionActivos?: boolean;
  avisosRechazoActivos?: boolean;
  destinatariosNotificacion?: DestinatariosNotificacion;
  metodosRecaudo: string[];
  // HU #10478 — opcionales: si se omiten el backend conserva el valor previo.
  runtFailoverTimeoutMs?: number;
  consultationProviderConfig?: ConsultationProviderConfig;
  // Feature #10707 — proveedores de avalúo (si se omite el backend conserva el valor previo).
  avaluoProviderConfig?: AvaluoProviderConfig;
  // FEATURE 02 — si se omite el backend conserva el valor previo.
  finesQuerySource?: FinesQuerySource;
  /** HU #11469 legado. */
  avisosCambioEstadoActivos?: boolean;
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

// ── Restricciones de consulta por OT (HU #10759/#10761) ─────────────────────
/** Consultas restringibles. `vehicle` queda fuera: rompería la hidratación del FUR. */
export type ConsultationRestrictionKind = "rnmc" | "fines";

/**
 * Fila de restricción configurada explícitamente. La tabla es DISPERSA: solo existen
 * filas para los pares que el admin tocó, y la ausencia de fila = consulta PERMITIDA.
 */
export interface OtConsultationRestriction {
  transitOfficeId: string;
  consultationKind: ConsultationRestrictionKind;
  enabled: boolean;
}

// ── Políticas de bloqueo de preflight por OT (FEATURE 05) ────────────────────
/**
 * Criterios del preflight cuyo carácter bloqueante (rojo) vs. informativo (amarillo) se
 * configura por compañía + OT. Ortogonal a {@link ConsultationRestrictionKind} (que decide
 * SI se consulta): este decide, para una consulta que corre, si su hallazgo negativo bloquea.
 */
export type BlockingCriterion = "soat" | "rtm" | "estado_vehiculo" | "fines" | "rnmc";

/**
 * Fila de política de bloqueo configurada explícitamente. Tabla DISPERSA: la ausencia de fila
 * = default del criterio ({@link BLOCKING_CRITERION_DEFAULTS}).
 */
export interface OtBlockingPolicy {
  transitOfficeId: string;
  criterion: BlockingCriterion;
  blocks: boolean;
}

/** Check activo ⇒ documento de prenda opcional para el par compañía+OT (default = obligatorio). */
export interface OtPrendaDocumentPolicy {
  transitOfficeId: string;
  documentOptional: boolean;
}

export interface OtPrendaDocumentPolicyCompany {
  tenantId: string;
  tenantName: string;
  documentOptional: boolean;
}

/**
 * Posición por defecto del toggle "¿bloquea el trámite?" cuando la compañía no configuró una fila
 * (par tenant, OT). Refleja el comportamiento PREVIO del trámite:
 *  - SOAT/RTM/estado: bloqueaban el pre-vuelo (rojo) → ON.
 *  - Comparendos: no bloquean la CREACIÓN (pre-vuelo amarillo), pero SÍ la RADICACIÓN al OT
 *    (gate del paso 4 de traspaso) → ON; apagarlo persiste blocks=false y también libera la
 *    radicación. (En backend: preflight usa DefaultBlocks=false, el gate usa override ?? true).
 *  - RNMC: siempre informativo → OFF.
 */
export const BLOCKING_CRITERION_DEFAULTS: Record<BlockingCriterion, boolean> = {
  soat: true,
  rtm: true,
  estado_vehiculo: true,
  fines: true,
  rnmc: false,
};

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

// ── Rastro unificado de auditoría administrativa/seguridad (HU #10680) ─────
// GET /api/v1/superadmin/audit — consulta global SuperAdmin (HU #10679, contrato
// `AdminAuditLogEntry` / `AdminAuditLogPageResponse`). Config + operaciones sobre
// usuarios/roles/permisos/autenticación.
export type AdminAuditTenantType = "COMPANY" | "TRANSIT_OFFICE";
export type AdminAuditModule = "users" | "roles" | "permissions" | "authentication" | "security" | "config";
export type AdminAuditResult = "success" | "failure";

export interface AdminAuditLogEntry {
  id: string;
  tenantId?: string | null;
  tenantType?: AdminAuditTenantType | null;
  module?: AdminAuditModule | null;
  entityName: string;
  operation?: string | null;
  result?: AdminAuditResult | null;
  errorCode?: string | null;
  /** Usuario actor (uuid) que ejecutó la operación. */
  changedBy?: string | null;
  targetEntityType?: string | null;
  /** Usuario/rol/permiso afectado (uuid) por la operación. */
  targetEntityId?: string | null;
  clientIp?: string | null;
  changedAt: string;
  /** Nombre del actor ya resuelto por el backend (antes solo llegaba el uuid `changedBy`). */
  changedByName?: string | null;
  changedByEmail?: string | null;
  /** Nombre de la entidad afectada (usuario o rol) ya resuelto. */
  targetName?: string | null;
  /** Campo modificado; en operaciones administrativas repite la operación. */
  fieldName?: string | null;
  /** Detalle del cambio serializado como JSON, cuando la operación lo registró. */
  oldValue?: string | null;
  newValue?: string | null;
}

export interface AdminAuditLogPageResponse {
  data: AdminAuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Query params del GET /api/v1/superadmin/audit (todos opcionales). */
export interface AdminAuditLogQuery {
  /** Matchea actor (changedBy) O afectado (targetEntityId). */
  userId?: string;
  tenantId?: string;
  tenantType?: AdminAuditTenantType;
  module?: AdminAuditModule;
  operation?: string;
  result?: AdminAuditResult;
  /** ISO date-time, límite inferior inclusive de changedAt. */
  dateFrom?: string;
  /** ISO date-time, límite superior inclusive de changedAt. */
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

// ── Analytics · Dashboard (HU #10243 / #10247) ──────────────────────────────
/** Categoría de trámite normalizada por el backend (RF01). */
export type AnalyticsCategory = "matriculas" | "traspasos" | "vehicular" | "otros";

/** Conteo de trámites en un estado concreto dentro de una categoría. */
export interface StatusCount {
  status: string;
  count: number;
}

/** Métricas agregadas de una categoría en el periodo consultado. */
export interface CategoryMetrics {
  category: AnalyticsCategory;
  total: number;
  byStatus: StatusCount[];
}

/** Respuesta de GET /api/v1/analytics/overview (RF01, RF02). */
export interface AnalyticsOverviewResponse {
  tenantId: string;
  from: string;
  to: string;
  categories: CategoryMetrics[];
}

/** Query params del overview. `tenantId` solo lo honra el backend para SuperAdmin (AC1). */
export interface AnalyticsOverviewParams {
  from: string;
  to: string;
  tenantId?: string;
}

/** Productividad de un radicador en el periodo (RF07). */
export interface TopProducer {
  userId: string;
  displayName: string;
  submittedCount: number;
  approvedCount: number;
  rejectedCount: number;
}

/** Respuesta de GET /api/v1/analytics/productivity/top (HU #10243). */
export interface TopProducersResponse {
  items: TopProducer[];
}

/** Query params del Top de productividad. */
export interface TopProducersParams {
  from: string;
  to: string;
  limit?: number;
  tenantId?: string;
}

/** Fila del detalle de trámites (RF04, RF05). */
export interface ProcedureDetail {
  id: string;
  referenceNumber: string;
  procedureTypeName: string;
  category: AnalyticsCategory;
  status: string;
  createdByDisplayName: string;
  submittedAt?: string | null;
  completedAt?: string | null;
}

/** Página de detalle de trámites de GET /api/v1/analytics/procedures (HU #10244). */
export interface ProcedureDetailsPage {
  items: ProcedureDetail[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Query params del detalle paginado. */
export interface ProcedureDetailsParams {
  from: string;
  to: string;
  category?: AnalyticsCategory;
  status?: string;
  page?: number;
  pageSize?: number;
  tenantId?: string;
}

/** Cuerpo del POST /api/v1/analytics/export/executive-pdf (HU #10246 / #10249). */
export interface ExecutivePdfParams {
  from: string;
  to: string;
  tenantId?: string;
}

// ── Analytics · Dashboard · Tendencia mensual ────────────────────────────────
/** Punto de tendencia mensual: total de trámites de una categoría en un mes. */
export interface MonthlyTrendPoint {
  year: number;
  month: number;
  category: AnalyticsCategory;
  total: number;
}

/** Respuesta de GET /api/v1/analytics/monthly-trend. */
export interface MonthlyTrendResponse {
  items: MonthlyTrendPoint[];
}

/** Query params del monthly-trend. */
export interface MonthlyTrendParams {
  from: string;
  to: string;
  tenantId?: string;
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

/** Error HTTP genérico que conserva el status y el cuerpo para distinguir casos (404, 409…). */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    /** Cuerpo JSON de la respuesta de error, si lo hubo (p. ej. el 409 con `procedureTypes`). */
    public readonly body?: unknown,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

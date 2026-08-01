/** Tipos del módulo Administración OT (HU #10215 / #10217 / #10218). */

export type OtOperationMode = "dashboard" | "quipux";

export interface OtFeatureFlag {
  id: string;
  flagKey: string;
  isEnabled: boolean;
  config: string;
}

export interface OtProfile {
  operationMode: OtOperationMode;
  quipuxReadOnly: boolean;
  transitOfficeId: string;
  featureFlags: OtFeatureFlag[];
}

export interface UpdateOtProfileRequest {
  operationMode?: OtOperationMode;
}

/** Requisitos configurables por OT (HU #10545 / #10546). */
export interface OtRequirements {
  transitOfficeId: string;
  requiresRnmc: boolean;
  allowPlatePreassign: boolean;
  identityValidationEnabled: boolean;
}

/** Los campos omitidos conservan su valor actual (conmutación independiente). */
export interface UpdateOtRequirementsRequest {
  requiresRnmc?: boolean;
  allowPlatePreassign?: boolean;
  identityValidationEnabled?: boolean;
}

export interface UpdateOtFeatureFlagRequest {
  isEnabled: boolean;
}

export interface OtClientProcedure {
  id: string;
  clientTenantId: string;
  procedureTypeId: string;
  procedureTypeName?: string;
  clientTenantName?: string;
  referenceNumber: string;
  status: string;
  /**
   * Sub-estado interno de la ruta de placa (null | preasignado | asignado | terminado),
   * ortogonal al status (que permanece en 'entregado').
   */
  plateFlowStatus?: string | null;
  /**
   * Estado del SOAT (null | unknown | vencido | vigente). Informativo; la decisión OT
   * en ruta de placa requiere `terminado`.
   */
  soatEstado?: string | null;
  /**
   * Dígito de preferencia de placa (0-9) indicado al radicar sin placa. Guía para el OT.
   */
  platePreferredLastDigit?: string | null;
  /** Check opcional del gestor; badge en bandeja OT solo en Terminado. */
  soatPagado?: boolean;
  /** Check opcional del gestor; badge en bandeja OT solo en Terminado. */
  impuestoDepartamentalPagado?: boolean;
  transitOfficeId?: string | null;
  createdAt: string;
  submittedAt?: string | null;
  /** HU #10536 — trámite marcado como prioritario: el OT lo revisa con primacía (solo indicador). */
  prioritario?: boolean;
  /** Propietario/vendedor (null en matrícula inicial). */
  vendedorNombre?: string | null;
  compradorNombre?: string | null;
  /** Gestor que radicó el trámite. */
  gestorNombre?: string | null;
  /** Detalle (GET by id): actores del trámite. */
  actors?: OtClientProcedureActor[];
  placa?: string | null;
  vin?: string | null;
  marca?: string | null;
  linea?: string | null;
  modelo?: string | null;
  color?: string | null;
  clase?: string | null;
  servicio?: string | null;
  combustible?: string | null;
}

export interface OtClientProcedureActor {
  actorType: string;
  documentType: string;
  documentNumber: string;
  fullName: string;
  email?: string | null;
  phone?: string | null;
  personType?: string | null;
}

export interface OtClientProcedurePagedResult {
  data: OtClientProcedure[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface OtClientProceduresParams {
  status?: string;
  procedureTypeId?: string;
  vin?: string;
  placa?: string;
  vendedor?: string;
  comprador?: string;
  gestor?: string;
  /** vin | placa | vendedor | comprador | gestor | createdAt | radicado | estado */
  sortBy?: string;
  /** asc | desc */
  sortDir?: "asc" | "desc";
  page?: number;
  pageSize?: number;
}

/** Diagnóstico de la bandeja OT (HU #10540/#10541 — R09): entregados con/sin grant vigente. */
export interface OtBandejaHealth {
  transitOfficeResolved: boolean;
  transitOfficeId: string | null;
  deliveredTotal: number;
  deliveredWithGrant: number;
  deliveredWithoutGrant: number;
  hasDeliveredWithoutGrant: boolean;
}

export interface OtWebhook {
  id: string;
  eventType: string;
  targetUrl: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateOtWebhookRequest {
  eventType: string;
  targetUrl: string;
  secret: string;
}

export interface UpdateOtWebhookRequest {
  targetUrl?: string;
  isActive?: boolean;
}

export interface OtWebhooksListResult {
  data: OtWebhook[];
}

export interface OtApiCallLog {
  endpoint: string;
  httpMethod: string;
  responseCode?: number | null;
  durationMs?: number | null;
  calledAt: string;
  correlationId?: string | null;
  payloadHash: string;
}

export interface OtApiLogsPagedResult {
  data: OtApiCallLog[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface OtApiLogsParams {
  direction?: string;
  from?: string;
  to?: string;
  minResponseCode?: number;
  page?: number;
  pageSize?: number;
}

export interface RejectOtClientProcedureRequest {
  reason: string;
}

export type OtRuleLogic = "AND" | "OR";

export type OtRuleActionType = "bloquear" | "biometria" | "cola_especial";

export interface OtRuleCondition {
  field: string;
  op: string;
  value: unknown;
}

export interface OtRuleAction {
  type: OtRuleActionType;
  queue_name?: string;
}

export interface OtRule {
  id: string;
  name: string;
  isEnabled: boolean;
  conditions: OtRuleCondition[];
  logic: OtRuleLogic;
  action: OtRuleAction;
}

export interface CreateOtRuleRequest {
  name: string;
  conditions: OtRuleCondition[];
  logic: OtRuleLogic;
  action: OtRuleAction;
}

export interface UpdateOtRuleRequest {
  isEnabled?: boolean;
}

export interface OtRulesListResult {
  data: OtRule[];
}

export interface OtDocumentPrecedenceItem {
  document_type_id: string;
  /** Código del catálogo (HU #11182); empareja con el tipo del adjunto del trámite. */
  document_code?: string;
  document_name: string;
  sort_order: number;
  /** HU #11181 — lo produce FLIT (FUR, certificados, mandato); el gestor no lo adjunta. */
  is_system_generated?: boolean;
  /** HU #11182 — el OT ya guardó una posición para este documento. */
  is_configured?: boolean;
}

export interface OtDocumentPrecedenceListResult {
  data: OtDocumentPrecedenceItem[];
}

export interface OtDocumentPrecedenceOrderItem {
  document_type_id: string;
  sort_order: number;
}

export interface UpdateOtDocumentPrecedenceRequest {
  procedure_type_id: string;
  items: OtDocumentPrecedenceOrderItem[];
}

export interface OtDocumentTag {
  id: string;
  code: string;
  name: string;
  color: string;
  /** Conteo local/FE para advertencia AC5 — no expuesto por API aún. */
  usageCount?: number;
}

export interface CreateOtDocumentTagRequest {
  code: string;
  name: string;
  color: string;
}

export interface OtDocumentTagsListResult {
  data: OtDocumentTag[];
}

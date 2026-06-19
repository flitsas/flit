// Tipos espejo de los DTOs de instancia de trámite (runtime/operación).
// La CONFIG dinámica (steps/sections/fields) se reutiliza desde
// procedure-parametrization.ts — aquí solo se modelan instancias y el stub semáforo.

import type {
  ProcedureFamily,
  ProcedureStep,
} from './procedure-parametrization';

export type InstanceStatus =
  | 'draft'
  | 'submitted'
  | 'in_review'
  | 'completed'
  | 'rejected';

/** Configuración pública por code: GET /procedure-types/{code}/configuration. */
export interface ProcedureConfiguration {
  id: string;
  code: string;
  name: string;
  family: ProcedureFamily;
  publishedAt: string | null;
  conformationRules: unknown[];
  steps: ProcedureStep[];
}

/** POST /tramites/instances — tenant viaja en el BODY (inconsistencia documentada). */
export interface CreateInstanceRequest {
  tenantId: string;
  procedureTypeId: string;
  createdByUserId: string;
  transitOfficeId?: string;
}

export interface ProcedureInstanceSummary {
  id: string;
  referenceNumber: string;
  status: InstanceStatus;
  procedureTypeId: string;
  tenantId: string;
  createdAt: string;
  submittedAt?: string | null;
}

export interface FieldValue {
  formFieldId: string;
  fieldKey: string;
  valueText: string | null;
  valueJson: string | null;
  source: string;
}

export interface StatusHistory {
  fromStatus: InstanceStatus | null;
  toStatus: InstanceStatus;
  changedAt: string;
  reason: string | null;
}

export interface Actor {
  actorType: string;
  documentType: string;
  documentNumber: string;
  fullName: string;
}

export interface ProcedureInstanceDetail {
  id: string;
  referenceNumber: string;
  status: InstanceStatus;
  procedureTypeId: string;
  tenantId: string;
  createdAt: string;
  submittedAt: string | null;
  completedAt: string | null;
  fieldValues: FieldValue[];
  statusHistory: StatusHistory[];
  actors: Actor[];
}

/** Item del body de PATCH /instances/{id}/field-values. */
export interface FieldValueInput {
  /** Nullable: el backend resuelve el campo por fieldKey si llega null. */
  formFieldId: string | null;
  fieldKey: string;
  valueText?: string | null;
  valueJson?: string | null;
}

// ── Actores del trámite (Slice 2) ──────────────────────────────────
// Contrato FIJO acordado con backend:
//   GET  /api/v1/tramites/instances/{id}/actors  -> { actors: ProcedureActor[] }
//   PUT  /api/v1/tramites/instances/{id}/actors  body { actors }
// La entidad `Actor` (arriba) es el espejo del detalle de instancia ya
// existente; estos tipos modelan la captura/edición dedicada de actores.

export type ActorRol = 'comprador' | 'vendedor';

export type ActorDocumentType = 'CC' | 'CE' | 'NIT' | 'PAS' | 'TI';

export interface ProcedureActor {
  rol: ActorRol;
  tipoDocumento: ActorDocumentType;
  numeroDocumento: string;
  nombreCompleto: string;
  email: string;
  telefono?: string;
}

/** Respuesta de GET /instances/{id}/actors. */
export interface ActorsResponse {
  actors: ProcedureActor[];
}

// ── Semáforo de consulta (stub #10201) ─────────────────────────────

export type PreflightCheckStatus = 'ok' | 'warn' | 'fail' | 'unknown';
export type PreflightOverall = 'green' | 'yellow' | 'red';

export interface PreflightAction {
  label: string;
  ctaId: string;
  href?: string;
}

export interface PreflightCheck {
  key: string;
  label: string;
  status: PreflightCheckStatus;
  source: string;
  message: string;
  action?: PreflightAction | null;
}

export interface PreflightSnapshot {
  overall: PreflightOverall;
  checks: PreflightCheck[];
  createdAt: string;
}

// ── Consulta real #10201: POST /instances/{id}/consultations/{templateCode} ──
// Tipos aditivos espejo del DTO ConsultationResult del backend.

export interface ConsultationCheck {
  key: string;
  label: string;
  status: PreflightCheckStatus;
  source: string;
  message?: string;
}

export interface ConsultationHydratedField {
  fieldKey: string;
  valueText?: string;
  valueJson?: string;
}

export interface ConsultationResult {
  provider: string;
  overall: PreflightOverall;
  checks: ConsultationCheck[];
  hydratedFields: ConsultationHydratedField[];
}

// ── Documentos / checklist del trámite (Slice 3) ───────────────────
// Contrato FIJO acordado con backend:
//   GET    /api/v1/tramites/instances/{id}/attachments   -> { attachments }
//   POST   /api/v1/tramites/instances/{id}/attachments   (multipart) -> AttachmentDto
//   DELETE /api/v1/tramites/instances/{id}/attachments/{attachmentId} -> 204
//   GET    /api/v1/tramites/instances/{id}/checklist     -> ChecklistView

/** Espejo del AttachmentDto del backend. */
export interface ProcedureAttachment {
  id: string;
  tipo: string;
  filename: string;
  mimetype: string;
  sizeBytes: number;
  sha256: string;
  source: string;
  uploadedAt: string;
}

/** Respuesta de GET /instances/{id}/attachments. */
export interface AttachmentsResponse {
  attachments: ProcedureAttachment[];
}

/** Item del checklist guiado por la tipología del trámite. */
export interface ChecklistItemView {
  key: string;
  label: string;
  obligatorio: boolean;
  docTipo?: string;
  satisfied: boolean;
}

/** Respuesta de GET /instances/{id}/checklist. */
export interface ChecklistView {
  items: ChecklistItemView[];
  faltanObligatorios: number;
  completo: boolean;
}

// ── Wizard diferenciado server-driven (Slice 4b) ───────────────────
// Contrato FIJO acordado con backend:
//   GET /api/v1/tramites/instances/{id}/wizard -> WizardState
// El backend manda el orden, status y razones de cada paso por modalidad
// (matrícula 5 pasos VIN-first / traspaso 6 pasos placa-first). La shell
// pinta lo que el backend decide; no recalcula gates en el cliente.

export type WizardModalidad = 'matricula_inicial' | 'traspaso';

export type WizardStepStatus = 'complete' | 'incomplete' | 'locked';

/** Keys canónicas por modalidad (matrícula: 5, traspaso: 6). */
export type WizardStepKey =
  // matrícula
  | 'consulta_vin'
  | 'documentos'
  | 'comprador'
  | 'identidad'
  | 'fur'
  // traspaso
  | 'consulta'
  | 'validacion'
  | 'vendedor'
  | 'comercial';

export interface WizardStep {
  index: number;
  key: WizardStepKey | string;
  label: string;
  status: WizardStepStatus;
  /** Códigos de razón de incompletitud (mapeados a copy en la UI). */
  reasons: string[];
}

/** Respuesta de GET /instances/{id}/wizard. */
export interface WizardState {
  modalidad: WizardModalidad;
  tipologiaCodigo: string;
  totalSteps: number;
  steps: WizardStep[];
  canSubmit: boolean;
  /** Códigos de bloqueo de envío (mapeados a copy en la UI). */
  blockers: string[];
}

// ── Datos comerciales (traspaso) — GET/PUT /instances/{id}/commercial ──

export type CommercialCausal =
  | 'COMPRAVENTA'
  | 'DONACION'
  | 'DACION_EN_PAGO'
  | 'ADJUDICACION';

export type CommercialMetodoPago = string;

export interface CommercialData {
  valorVenta: number | null;
  causal: CommercialCausal | null;
  tasaImpuesto: number | null;
  derechos: number | null;
  metodoPago: CommercialMetodoPago | null;
}

// ── Biométrica (Slice 6) ────────────────────────────────────────────
// Contrato FIJO acordado con backend:
//   POST /api/v1/tramites/instances/{id}/biometric  -> IniciarBiometriaResult (201)
//   GET  /api/v1/tramites/instances/{id}/biometric  -> { validations }
//   GET  /api/v1/public/biometric/{token}           -> BiometriaPublicView
//   POST /api/v1/public/biometric/{token} (multipart: rostro|cedula_frontal|cedula_reverso)
//        -> CompletarBiometriaResult
// La parte es null en matrícula (única parte = comprador) y 'comprador'|'vendedor'
// en traspaso. El status/gating lo decide el wizard server-driven (no se calcula aquí).

/** Estados posibles de una validación biométrica (espejo de BiometricEstados). */
export type BiometricEstado =
  | 'enviado'
  | 'en_proceso'
  | 'aprobado'
  | 'rechazado'
  | 'expirado';

/** Parte a la que pertenece la validación. null = matrícula (comprador único). */
export type BiometricParte = 'comprador' | 'vendedor';

/** Tipos de documento admitidos por la captura biométrica. */
export type BiometricTipoDoc = 'CC' | 'CE' | 'TI' | 'PPT' | 'PAS';

/** Espejo de BiometricValidationDto (vista del gestor autenticado). */
export interface BiometricValidation {
  id: string;
  parte: BiometricParte | null;
  nombre: string;
  tipoDoc: string;
  documento: string;
  email: string;
  estado: BiometricEstado;
  intentos: number;
  maxIntentos: number;
  score: number | null;
  expiresAt: string;
  validadoAt: string | null;
  expired: boolean;
}

/** Entrada para iniciar una biométrica (espejo de IniciarBiometriaInput). */
export interface IniciarBiometriaInput {
  parte?: BiometricParte | null;
  nombre: string;
  tipoDoc: string;
  documento: string;
  email: string;
}

/** Resultado de iniciar: incluye el token CRUDO y el path del magic-link. */
export interface IniciarBiometriaResult {
  validation: BiometricValidation;
  token: string;
  magicLinkPath: string;
}

/** Respuesta de GET /instances/{id}/biometric. */
export interface BiometricValidationsResponse {
  validations: BiometricValidation[];
}

/** Vista PÚBLICA por token (sin PII sensible). Espejo de BiometriaPublicViewDto. */
export interface BiometriaPublicView {
  estado: BiometricEstado;
  parte: BiometricParte | null;
  nombre: string;
  intentos: number;
  maxIntentos: number;
  expired: boolean;
}

/** Resultado de completar la biométrica (espejo de CompletarBiometriaResult). */
export interface CompletarBiometriaResult {
  estado: BiometricEstado;
  score: number;
  motivo: string;
}

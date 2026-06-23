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

/**
 * POST /tramites/instances — tenant viaja en el BODY (inconsistencia documentada).
 *
 * Entrada por MODALIDAD (M0): el backend deriva la tipología desde `modalidad`,
 * por lo que `procedureTypeId` ya NO es obligatorio cuando se envía `modalidad`.
 * Se mantiene `procedureTypeId` opcional para el flujo legacy (selector de tipos
 * publicados) que aún cubren los tests del wizard.
 */
export interface CreateInstanceRequest {
  tenantId: string;
  createdByUserId: string;
  modalidad?: WizardModalidad;
  procedureTypeId?: string;
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

// ── Listado de instancias (Slice M6) ───────────────────────────────
// Contrato FIJO acordado con backend:
//   GET /api/v1/tramites/instances  (X-Tenant-Id)  -> { items: InstanceSummary[] }
// Resumen pensado para la tabla de "Trámites en curso" de OperacionView:
// trae placa/VIN/vehículo/comprador desnormalizados + progreso del wizard.
export interface InstanceSummary {
  id: string;
  referenceNumber: string;
  modalidad: WizardModalidad;
  estado: InstanceStatus;
  placa: string | null;
  vin: string | null;
  vehiculoMarca: string | null;
  vehiculoLinea: string | null;
  compradorNombre: string | null;
  compradorDocumento: string | null;
  pasoActual: number;
  totalPasos: number;
  createdAt: string;
}

/** Respuesta de GET /instances. */
export interface InstancesResponse {
  items: InstanceSummary[];
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
  /** Persistidos en actor.metadata (JSON) — opcionales. */
  ciudad?: string;
  direccion?: string;
}

/** Respuesta de GET /instances/{id}/actors. */
export interface ActorsResponse {
  actors: ProcedureActor[];
}

// ── Autopopulado desde RUNT (Slice M3) ─────────────────────────────
// POST /instances/{id}/runt-person  body { documentType, documentNumber }
// Siempre 200 ante una petición válida; `found` indica si RUNT halló a la
// persona. Si no, el usuario completa los datos manualmente (fallback).
export interface RuntPersonLookupInput {
  documentType: ActorDocumentType;
  documentNumber: string;
}

export interface RuntPersonLookupResult {
  found: boolean;
  fullName: string | null;
  firstName: string | null;
  lastName: string | null;
  documentType: string;
  documentNumber: string;
  licenseStatus: string | null;    // driverStatus del conductor
  source: 'RUNT';
  mode: 'real' | 'mock';
  // Campos enriquecidos (presentes cuando found=true)
  citizenStatus?: string | null;    // Estado del ciudadano (ACTIVA/INACTIVA)
  hasPendingFines?: boolean;        // true si tieneMultas == "SI"
  nroPazYSalvo?: string | null;     // Número del paz y salvo
  hasActiveLicense?: boolean;       // true si tiene al menos 1 licencia ACTIVA
  licenseCategories?: string | null; // "B1" o "B1,C1"
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

// ── Firma electrónica (Slice 7A) ────────────────────────────────────
// Contrato FIJO acordado con backend:
//   POST /api/v1/tramites/instances/{id}/signatures            -> SignatureDto (201)
//   GET  /api/v1/tramites/instances/{id}/signatures            -> { signatures }
//   POST /api/v1/tramites/instances/{id}/signatures/{sigId}/simulate -> SimularFirmaResult
// La firma de la compraventa SOLO aplica a traspaso (matrícula → 409 no_aplica).

/** Parte que firma la compraventa. */
export type SignatureParte = 'comprador' | 'vendedor';

/** Estados de una firma electrónica (espejo de SignatureEstados). */
export type SignatureEstado =
  | 'pendiente_envio'
  | 'enviada'
  | 'firmada'
  | 'rechazada';

/** Espejo de SignatureDto (vista del gestor autenticado). */
export interface Signature {
  id: string;
  parte: string;
  docTipo: string;
  proveedor: string;
  estado: string;
  envelopeId: string | null;
  signUrl: string | null;
  firmada: boolean;
  solicitadoAt: string;
  firmadoAt: string | null;
}

/** Respuesta de GET /instances/{id}/signatures. */
export interface SignaturesResponse {
  signatures: Signature[];
}

/** Entrada para solicitar la firma de una parte (espejo de SolicitarFirmaInput). */
export interface SolicitarFirmaInput {
  parte: string;
  docTipo?: string | null;
}

/** Resultado de simular la firma (mock complete). */
export interface SimularFirmaResult {
  id: string;
  estado: string;
  pdfPath: string | null;
  sha256: string | null;
}

// ── FUR / compraventa (Slice 7A) ────────────────────────────────────
// Contrato FIJO acordado con backend:
//   POST /api/v1/tramites/instances/{id}/fur -> { documents } (201)
//   409 biometria_gate si la biométrica requerida no está aprobada.
// Los documentos generados se listan/descargan vía el endpoint de adjuntos
// (GET /instances/{id}/attachments — tipos 'fur' / 'compraventa').

/** Un documento generado (FUR / compraventa) referenciado al adjunto persistido. */
export interface FurDocument {
  attachmentId: string;
  tipo: string;
  filename: string;
  sha256: string;
}

/** Respuesta de POST /instances/{id}/fur. */
export interface GenerarFurResult {
  documents: FurDocument[];
}

// ── Participantes del portal (Slice 7B) — lado gestor autenticado ───
// Contrato FIJO acordado con backend:
//   POST /api/v1/tramites/instances/{id}/participants               -> InvitarParticipanteResult (201)
//   GET  /api/v1/tramites/instances/{id}/participants               -> { participants }
//   POST /api/v1/tramites/instances/{id}/participants/{pid}/reinvite -> InvitarParticipanteResult

/** Roles admitidos para un participante del portal. */
export type ParticipantRol = 'comprador' | 'vendedor' | 'mandatario';

/** Espejo de ParticipantDto (vista del gestor autenticado). */
export interface Participant {
  id: string;
  rol: string;
  nombre: string;
  email: string;
  telefono: string | null;
  whatsappOptIn: boolean;
  consentDado: boolean;
  consentVersion: string | null;
  consent1581At: string | null;
  expiresAt: string;
  completedAt: string | null;
  lastReminderAt: string | null;
  expirado: boolean;
  completado: boolean;
}

/** Resultado de invitar/reinvitar: incluye el token CRUDO (solo aquí). */
export interface InvitarParticipanteResult {
  participant: Participant;
  token: string;
  magicLinkPath: string;
}

/** Respuesta de GET /instances/{id}/participants. */
export interface ParticipantsResponse {
  participants: Participant[];
}

/** Entrada para invitar a un participante (espejo de InvitarParticipanteInput). */
export interface InvitarParticipanteInput {
  rol: string;
  nombre: string;
  email: string;
  telefono?: string | null;
  whatsappOptIn: boolean;
}

// ── Portal público del participante (Slice 7B) ───────────────────────
// Contrato FIJO acordado con backend (sin auth, token = credencial):
//   GET  /api/v1/public/portal/{token}              -> PortalViewDto
//   POST /api/v1/public/portal/{token}/consent      -> AceptarConsentimientoResult
//   POST /api/v1/public/portal/{token}/documentos   (multipart file+tipo) -> AttachmentDto
//   GET  /api/v1/public/portal/{token}/firma        -> PortalFirmaUrlDto
//   POST /api/v1/public/portal/{token}/firma/simulate -> SimularFirmaResult
//   POST /api/v1/public/portal/{token}/finalizar    -> FinalizarPortalResult
// SEGURIDAD: token inválido/expirado/usado → 404 not_found genérico.

/** Resumen mínimo de la instancia para el portal. */
export interface PortalInstanceSummary {
  referencia: string;
  modalidadEntrada: string;
  tipologiaCodigo: string | null;
  tipologiaNombre: string | null;
}

/** Estado de un documento requerido para el rol del participante. */
export interface PortalDocumentoStatus {
  tipo: string;
  label: string;
  obligatorio: boolean;
  subido: boolean;
}

/** Paso de biométrica del participante. */
export interface PortalBiometricaStatus {
  existe: boolean;
  estado: string | null;
  pendiente: boolean;
}

/** Paso de firma del participante. */
export interface PortalFirmaStatus {
  aplica: boolean;
  existe: boolean;
  estado: string | null;
  firmada: boolean;
}

/** Pasos pendientes agregados para el rol del participante. */
export interface PortalPasosPendientes {
  consentDado: boolean;
  documentos: PortalDocumentoStatus[];
  biometrica: PortalBiometricaStatus;
  firma: PortalFirmaStatus;
  completado: boolean;
}

/** Vista PÚBLICA del portal (espejo de PortalViewDto). */
export interface PortalView {
  rol: string;
  nombre: string;
  consentDado: boolean;
  consentVersion: string;
  consentText: string;
  expirado: boolean;
  completado: boolean;
  instancia: PortalInstanceSummary;
  pasosPendientes: PortalPasosPendientes;
}

/** Resultado de aceptar el consentimiento Ley 1581. */
export interface AceptarConsentimientoResult {
  consentVersion: string;
  consent1581At: string;
}

/** Estado/URL de firma del participante en el portal (espejo de PortalFirmaUrlDto). */
export interface PortalFirmaUrl {
  aplica: boolean;
  signatureId: string | null;
  estado: string | null;
  signUrl: string | null;
  firmada: boolean;
}

/** Resultado de finalizar la participación. */
export interface FinalizarPortalResult {
  completedAt: string;
}

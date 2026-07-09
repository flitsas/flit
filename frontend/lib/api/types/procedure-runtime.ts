// Tipos espejo de los DTOs de instancia de trámite (runtime/operación).
// La CONFIG dinámica (steps/sections/fields) se reutiliza desde
// procedure-parametrization.ts — aquí solo se modelan instancias y el stub semáforo.

import type {
  ProcedureFamily,
  ProcedureStep,
} from './procedure-parametrization';

// N 03 (ADR-0022) — estados de NEGOCIO del trámite, vocabulario único de la API.
// Fuente de verdad de labels/estilos: lib/tramites/estados.ts.
export type InstanceStatus =
  | 'borrador'
  | 'anulado'
  | 'preparado'
  | 'entregado'
  | 'aprobado'
  | 'rechazado';

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
  /** HU #10350 — sello de borrador finalizado (datos completos a la espera de identidad async). */
  draftFinalizedAt?: string | null;
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
  organismoTransito: string | null;
  pasoActual: number;
  totalPasos: number;
  createdAt: string;
  // HU #10350 — desacople de la validación de identidad async. Derivan los chips del listado
  // ("Pendiente validación" / "Pendiente firma") y la acción de la fila ("Radicar"/"Continuar").
  /** Sello de borrador finalizado; null si el borrador no se ha finalizado. */
  draftFinalizedAt: string | null;
  /** Estado agregado de identidad: 'aprobado' | 'en_proceso' | 'rechazado' | null (sin iniciar). */
  identityValidationStatus: string | null;
  /** Traspaso: firma de la compraventa de alguna parte aún pendiente. */
  signaturePending: boolean;
  /** Gates de radicación satisfechos (mismo cómputo que el wizard). */
  canSubmit: boolean;
  /** HU #10536 — marcado prioritario por el gestor: el OT lo revisa con primacía (ordenamiento). */
  prioritario: boolean;
  /** Compañía dueña (#1): para abrir el trámite como SuperAdmin y para la columna/filtro Compañía. */
  tenantId: string;
  /** Razón social de la compañía; solo presente en el listado multi-tenant del SuperAdmin. */
  companiaNombre: string | null;
}

/** Respuesta de GET /instances. */
export interface InstancesResponse {
  items: InstanceSummary[];
}

/** Organismo de tránsito habilitado para la empresa (catálogo + grant). */
export interface TransitOfficeOption {
  id: string;
  code: string;
  name: string;
  cityCode: string;
}

export interface TransitOfficesResponse {
  items: TransitOfficeOption[];
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
  /** HU #10350 — sello de borrador finalizado; controla el modo readOnly parcial del wizard. */
  draftFinalizedAt?: string | null;
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

/**
 * Tipo de persona del actor (HU #10542/#10543). Para persona natural, el documento de
 * identidad se incorpora desde la validación biométrica y el checklist no ofrece la carga
 * manual de cédula; persona jurídica la conserva.
 */
export type ActorPersonType = 'natural' | 'juridical';

// HU #10478 — proveedor primario de consulta resuelto para el tenant, por tipo. El wizard lo usa para
// adaptar la UI (p. ej. en traspaso ocultar el tipo de documento del propietario cuando el proveedor de
// placa es Kyverum RUNT, que lo resuelve solo y lo devuelve en la respuesta).
export interface ConsultationProvidersConfig {
  vehicleVin: string;
  vehiclePlate: string;
  conductor: string;
}

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
  /**
   * Tipo de persona (HU #10543). Persona natural omite la carga manual de cédula en el
   * checklist (el documento llega desde la validación de identidad).
   */
  personType?: ActorPersonType;
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

// 'error' = un proveedor no se pudo verificar (no-200/timeout): bloqueo DURO, no subsanable con
// "aceptar riesgo" (a diferencia de 'fail', que sí es subsanable). Se pinta rojo como 'fail'.
export type PreflightCheckStatus = 'ok' | 'warn' | 'fail' | 'unknown' | 'error';
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

/**
 * Respuesta de POST /instances/{id}/attachments/presign: POST policy de S3 para subir el binario
 * directo desde el navegador. `storagePath` (id del file-manager) se devuelve luego al registrar la
 * metadata; `fields` son los campos firmados que van ANTES del 'file' en el multipart a S3.
 */
export interface PresignAttachmentResponse {
  storagePath: string;
  url: string;
  fields: Record<string, string>;
}

/**
 * Respuesta de POST /instances.../ocr/{tipo}: análisis semántico del documento con el modelo de
 * visión, ANTES de subirlo al expediente. No persiste nada en el backend.
 */
export interface DocumentOcrResult {
  ok: boolean;
  tipo: string;
  /** JSON extraído por el modelo (campos según el tipo). null si no se pudo interpretar. */
  data: Record<string, unknown> | null;
  /**
   * PDF recortado (base64) cuando el documento ocupaba sólo un subconjunto de páginas de un PDF
   * multi-documento; null/ausente si no hubo recorte. El wizard sube este recorte en vez del original.
   */
  extractedPdfBase64?: string | null;
}

/** Item del checklist guiado por la tipología del trámite. */
export interface ChecklistItemView {
  key: string;
  label: string;
  obligatorio: boolean;
  docTipo?: string;
  satisfied: boolean;
  /** RF09 — tamaño máximo por tipo (bytes). Ausente ⇒ usar el límite global. */
  maxSizeBytes?: number;
  /** RF08 — formatos MIME permitidos por tipo. Ausente/vacío ⇒ formatos globales. */
  mimeTypesAllowed?: string[];
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
  /** N 03 — estado de negocio actual del trámite (borrador|anulado|preparado|entregado|aprobado|rechazado). */
  status: InstanceStatus | string;
  /** N 03 — transiciones permitidas por la máquina de estados (el backend manda). */
  allowedTransitions: string[];
  /**
   * HU #10549 — si el OT destino tiene la validación de identidad deshabilitada es `false` y el
   * wizard oculta el paso de identidad. Ausente/true ⇒ se exige (comportamiento por defecto).
   */
  identityValidationEnabled?: boolean;
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

// ── Prenda / gravamen (IT-3, Feature #10585) ─────────────────────────
//   PUT /api/v1/tramites/instances/{id}/prenda -> PrendaData
//   GET /api/v1/tramites/instances/{id}/prenda -> PrendaData | null
export type PrendaDecision =
  | 'solicitar'
  | 'registrar'
  | 'levantar'
  | 'omitir'
  | 'sin_prenda';

/** Decisión de prenda vigente del trámite (o null si no se ha registrado ninguna). */
export interface PrendaData {
  id: string;
  decision: PrendaDecision;
  estado: 'vigente' | 'reemplazada';
  acreedorNombre: string | null;
  acreedorDocumento: string | null;
  createdAt: string;
}

/** Payload de PUT /prenda. */
export interface PrendaInput {
  decision: PrendaDecision;
  acreedorNombre?: string | null;
  acreedorDocumento?: string | null;
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
  | 'expirado'
  // Cola de envío (provider-agnostic): el envío al proveedor falló y se reintenta / agotó intentos.
  | 'pendiente_envio'
  | 'error_envio';

/** Parte a la que pertenece la validación. null = matrícula (comprador único). */
export type BiometricParte = 'comprador' | 'vendedor';

/** Proveedor de validación de identidad (espejo de BiometricProviders). */
export type BiometricProvider = 'mock' | 'kyverum';

/** Estado de vigencia derivado de una identidad aprobada (espejo de BiometricVigenciaEstados). */
export type BiometricVigenciaEstado = 'vigente' | 'por_vencer' | 'vencida';

/** Tipos de documento admitidos por la captura biométrica. */
export type BiometricTipoDoc = 'CC' | 'CE' | 'TI' | 'PPT' | 'PAS';

/** Espejo de BiometricValidationDto (vista del gestor autenticado). */
export interface BiometricValidation {
  id: string;
  partyRole: BiometricParte | null;
  name: string;
  documentType: string;
  documentNumber: string;
  email: string;
  status: BiometricEstado;
  intentos: number;
  maxIntentos: number;
  score: number | null;
  expiresAt: string;
  validatedAt: string | null;
  expired: boolean;
  // HU #10233: proveedor de la validación y URL de captura (solo kyverum + en_proceso).
  provider: string;
  captureUrl: string | null;
  // HU #10234 (AC4): motivo de rechazo sanitizado (solo estado rechazado). Opcional por compat.
  rejectionReason?: string | null;
  // Motivo del ÚLTIMO intento fallido mientras la validación sigue ABIERTA (en_proceso): Kyverum permite
  // reintentar. Guía amigable de Kyverum (p.ej. "rostro no completamente visible"). Null si no aplica.
  ultimoIntentoMotivo?: string | null;
}

/**
 * Resultado de reconciliar una validación con el proveedor (POST .../biometric/{id}/reconcile):
 * consulta el estado real en Kyverum y lo aplica si ya es terminal. `updated` = hubo cambio.
 */
export interface ReconcileIdentityResult {
  status: BiometricEstado;
  updated: boolean;
}

/**
 * Un evento de la bitácora (solo lectura) del ciclo de una validación de identidad: envío, llegada del
 * webhook, si descifró el secreto, firma, resultado y reconciliaciones. Sin PII ni secretos. Espejo de
 * IdentityAuditEventDto del backend. Diagnóstico de "qué pasó" sin entrar a la BD/pod (solo soporte).
 */
export interface IdentityAuditEvent {
  occurredAt: string;
  stage: string;
  outcome: string;
  httpStatus: number | null;
  signaturePresent: boolean | null;
  secretPresent: boolean | null;
  decryptOk: boolean | null;
  providerStatus: string | null;
  errorType: string | null;
  message: string | null;
}

/** Respuesta de GET .../biometric/{validationId}/audit (espejo de IdentityAuditResponse). */
export interface IdentityAuditResponse {
  validationId: string;
  events: IdentityAuditEvent[];
  /**
   * true cuando la identidad está reutilizada de otro trámite del mismo cliente (HU #10350): la
   * bitácora es la real de esa validación, pero corresponde al trámite donde se realizó. La UI lo
   * explica en vez de mostrar un error.
   */
  referencedFromOtherProcedure?: boolean;
}

/**
 * Entrada para iniciar una biométrica (espejo de IniciarBiometriaInput). Los datos del sujeto son
 * OPCIONALES: si no se envían, el backend los resuelve desde el actor de la parte registrado en el
 * trámite (el wizard envía solo `parte`). Enviarlos los usa como override (API/Postman directo).
 */
export interface IniciarBiometriaInput {
  parte?: BiometricParte | null;
  nombre?: string;
  tipoDoc?: string;
  documento?: string;
  email?: string;
}

/**
 * Resultado de iniciar. Mock → token CRUDO + magicLinkPath (3 fotos). Kyverum → captureUrl
 * (captura remota); token/magicLinkPath ausentes. En ambos, validation.captureUrl también trae la URL
 * cuando aplica.
 */
export interface IniciarBiometriaResult {
  validation: BiometricValidation;
  token?: string;
  magicLinkPath?: string;
  captureUrl?: string;
}

/** Respuesta de GET /instances/{id}/biometric. `provider` = proveedor configurado (mock|kyverum). */
export interface BiometricValidationsResponse {
  validations: BiometricValidation[];
  provider: string;
}

/**
 * Espejo de TenantBiometricValidationDto (HU #10234): fila de la vista transversal del submódulo
 * "Validaciones de Identidad". Incluye el trámite al que pertenece (para navegar). Sin email ni
 * captureUrl (vista de monitoreo, no de gestión de la captura).
 */
export interface TenantBiometricValidation {
  id: string;
  instanceId: string;
  referenceNumber: string;
  modalidad: string;
  partyRole: BiometricParte | null;
  name: string;
  documentType: string;
  documentNumber: string;
  status: BiometricEstado;
  score: number | null;
  provider: string;
  expired: boolean;
  rejectionReason?: string | null;
  /** Fecha de registro (creación) de la validación. */
  createdAt: string;
  /** Fecha de aprobación (null si aún no se aprobó). */
  validatedAt: string | null;
  /** Fecha de fin de vigencia (aprobación + 30 días). Null si no hay aprobación. */
  validUntil: string | null;
  /** Días calendario de vigencia restantes (0 si venció). Null si no hay aprobación. */
  daysRemaining: number | null;
}

/** KPIs agregados del submódulo de Validaciones (espejo de BiometricValidationStatsDto). */
export interface BiometricValidationStats {
  total: number;
  aprobadas: number;
  enProceso: number;
  rechazadas: number;
  expiradas: number;
}

/** Respuesta de GET /tramites/biometric-validations: filas de la página + KPIs + metadatos de paginación. */
export interface TenantBiometricValidationsResponse {
  validations: TenantBiometricValidation[];
  stats: BiometricValidationStats;
  /** Página devuelta (1-based). */
  page: number;
  /** Filas por página efectivas (acotadas a [10, 50]). */
  pageSize: number;
  /** Total del conjunto filtrado completo (para calcular el nº de páginas). */
  total: number;
}

/**
 * Filtros del listado transversal de validaciones (HU #10348 → query params del backend HU #10347).
 * Todos opcionales; los vacíos/undefined no se envían como query param. El backend combina con AND y
 * devuelve filas + KPIs del mismo subconjunto. `motivoRechazo` solo aplica a rechazadas (filtrado en
 * memoria sobre el texto sanitizado). Fechas en ISO-8601. Puede responder 400 si `estado/provider/parte`
 * está fuera de catálogo, `scoreMin > scoreMax` o `createdFrom > createdTo`.
 */
export interface TenantBiometricValidationFilters {
  referenceNumber?: string;
  modalidad?: WizardModalidad;
  name?: string;
  partyRole?: BiometricParte;
  documentType?: string;
  documentNumber?: string;
  status?: BiometricEstado;
  provider?: BiometricProvider;
  scoreMin?: number;
  scoreMax?: number;
  createdFrom?: string;
  createdTo?: string;
  rejectionReason?: string;
  /** Estado de vigencia de la identidad aprobada: vigente | por_vencer | vencida. */
  vigenciaEstado?: BiometricVigenciaEstado;
  /** Fin de vigencia (aprobación + 30 días) desde / hasta, en ISO-8601. */
  expiraDesde?: string;
  expiraHasta?: string;
  /** "Vence en ≤ N días": identidades vigentes que vencen en N días calendario o menos. */
  venceEnDias?: number;
  /** Página (1-based). */
  page?: number;
  /** Filas por página (10–50). */
  pageSize?: number;
}

/** Cola en dead-letter de una validación atascada. `envio` = el envío al proveedor (Kyverum) agotó
 * reintentos (estado error_envio); `encadenamiento` = el encadenamiento async firma/FUR agotó reintentos. */
export type StuckIdentityValidationKind = 'envio' | 'encadenamiento';

/**
 * Validación de identidad ATASCADA (dead-letter): agotó los reintentos automáticos de su cola — el ENVÍO al
 * proveedor (kind=envio) o el ENCADENAMIENTO async firma/FUR (kind=encadenamiento). Espejo de
 * StuckIdentityValidationDto (HU #10349). Sin PII.
 */
export interface StuckIdentityValidation {
  id: string;
  validationId: string;
  eventType: string;
  attempts: number;
  occurredAt: string;
  createdAt: string;
  // Persona validada (la UI muestra nombre + últimos 4 del documento). Null si la validación ya no existe.
  name: string | null;
  documentType: string | null;
  documentNumber: string | null;
  // Qué cola se atascó (para etiquetar la fila). Backend siempre lo envía; opcional por tolerancia a
  // un backend en transición que aún no lo exponga (default 'encadenamiento' en la UI).
  kind?: StuckIdentityValidationKind;
}

/** Respuesta de GET /identity-validation/stuck: eventos atascados + total + tope de reintentos. */
export interface StuckIdentityValidationsResponse {
  stuck: StuckIdentityValidation[];
  total: number;
  maxDeliveryAttempts: number;
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

/**
 * HU #10350 — desenlace de "asegurar identidad" de una parte al guardarla (espejo de
 * EnsureIdentityResult). El backend reutiliza una validación vigente o indica que se requiere validar.
 */
export type EnsureIdentityOutcome =
  | 'ya_vigente'           // el trámite ya tiene una validación aprobada y vigente
  | 'en_proceso'           // ya hay una validación en curso
  | 'reusada'              // se clonó una validación vigente de la persona (identidad aprobada)
  | 'requiere_validacion'  // no hay vigente → el front dispara la validación automáticamente
  | 'sin_actor';           // la parte aún no tiene actor con documento

export interface EnsureIdentityResult {
  outcome: EnsureIdentityOutcome;
  validationId?: string | null;
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

// ── Impronta integrada al trámite (paso FUR) ─────────────────────────
// POST /api/v1/tramites/instances/{id}/attachments/generate-impronta -> GenerarImprontaAttachmentResult (201)
// Genera el Certificado de Improntas Digitales (Kyverum RUNT) con los datos del trámite y lo
// adjunta como documento tipo 'impronta' (mismo flujo que una subida manual).

/** Respuesta de POST /instances/{id}/attachments/generate-impronta. */
export interface GenerarImprontaAttachmentResult {
  attachmentId: string;
  filename: string;
  sha256: string;
  radicado: string;
  hash: string;
}

// ── Expediente consolidado (matrícula inicial) ───────────────────────
// POST /api/v1/tramites/instances/{id}/consolidado -> { document } (201)
// Fusiona FUR + certificado de identidad + adjuntos del trámite en un PDF único.

export interface ConsolidadoDocument {
  attachmentId: string;
  tipo: string;
  filename: string;
  sha256: string;
}

export interface GenerarConsolidadoResult {
  document: ConsolidadoDocument;
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

// ── HU-2 (N03, RF05) — historial de transiciones de estado ─────────────────

/** Fila del historial de transiciones (GET /instances/{id}/status-history). */
export interface StatusHistoryItem {
  id: string;
  fromStatus: string | null;
  toStatus: string;
  changedAt: string;
  changedByUserId: string | null;
  changedByName: string | null;
  reason: string | null;
}

/** Página del historial: más reciente primero. */
export interface StatusHistoryPage {
  items: StatusHistoryItem[];
  total: number;
  page: number;
  pageSize: number;
}

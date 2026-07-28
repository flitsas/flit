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
  | 'rechazado'
  // HU #10870 — reabre la edición de un entregado/rechazado sin volver a borrador; re-radicar
  // (subsanacion → entregado) es la única transición permitida desde aquí (HU #10874, AC2).
  | 'subsanacion';

/**
 * Sub-estado INTERNO de la ruta de placa (Feature #10587 / HU #10785), ORTOGONAL a
 * {@link InstanceStatus}: mientras avanza, el trámite permanece en `entregado`. `null`/ausente =
 * trámite sin ruta de placa. Gobierna el badge secundario, el panel de SOAT y las acciones del OT.
 */
export type PlateFlowStatus = 'preasignado' | 'asignado';

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

/**
 * CF-02 (HU #10879/#10883) — datos del vehículo capturados en el PASO 1, cuando el trámite todavía
 * no existe. Alimentan tanto la consulta desacoplada (`runPreflightPreview`) como la creación al
 * avanzar al paso 2 (`createInstanceFromConsulta`).
 */
export interface ConsultaVehiculoInput {
  modalidad: WizardModalidad;
  vin?: string | null;
  plate?: string | null;
  ownerDocumentType?: string | null;
  ownerDocumentNumber?: string | null;
}

/**
 * Resultado de la consulta del paso 1 SIN trámite creado. `previewToken` se devuelve al backend al
 * avanzar al paso 2 para que la creación reuse esta consulta en vez de repetirla contra el RUNT.
 */
export interface PreflightPreviewResult {
  previewToken: string;
  preflight: PreflightSnapshot;
  /** Atributos del vehículo hidratados por la consulta, en la forma que ya pinta el wizard. */
  vehicleFields: FieldValue[];
}

/** Trámite recién creado al avanzar al paso 2, con su preflight ya persistido. */
export interface CreateFromConsultaResult {
  instance: ProcedureInstanceSummary;
  preflight: PreflightSnapshot | null;
}

export interface ProcedureInstanceSummary {
  id: string;
  referenceNumber: string;
  status: InstanceStatus;
  /** Feature #10587 / HU #10785 — sub-estado interno de placa (null | preasignado | asignado). */
  plateFlowStatus?: PlateFlowStatus | null;
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
  /** Feature #10587 / HU #10785 — sub-estado interno de placa (null | preasignado | asignado). */
  plateFlowStatus?: PlateFlowStatus | null;
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
  /**
   * HU #10871/#10872 (backend) — observación de subsanación serializada como JSON en
   * `procedure_instance_status_history.metadata`. `GetProcedureInstanceHandler.ToDetail`
   * (commit f3b64f5e) la expone filtrada a `{motivo, items:[{campo,detalle}]}`; por
   * seguridad/Habeas Data NO incluye `fieldSnapshot` ni los tenant ids. Llega `null` en
   * entradas sin observación (p. ej. aprobar/rechazar); `lib/tramites/subsanacion.ts` degrada
   * entonces al `reason` plano (ver SubsanacionPanel).
   */
  metadata?: string | null;
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
  /** Feature #10587 / HU #10785 — sub-estado interno de placa (null | preasignado | asignado). */
  plateFlowStatus?: PlateFlowStatus | null;
  procedureTypeId: string;
  tenantId: string;
  createdAt: string;
  submittedAt: string | null;
  completedAt: string | null;
  /** HU #10350 — sello de borrador finalizado; controla el modo readOnly parcial del wizard. */
  draftFinalizedAt?: string | null;
  /**
   * HU #10879/#10883 — paso actual PERSISTIDO del wizard (autosave por paso). `null`/ausente ⇒ el
   * frontend cae al paso derivado de los gates (comportamiento previo).
   */
  currentStep?: string | null;
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
  // FEATURE 02 — política "solo vehículos propios" del tenant. Cuando es true, el wizard autorrellena
  // el documento del tenant (NIT) en la consulta de traspaso y bloquea la consulta si se edita a otro.
  onlyOwnVehicles: boolean;
}

/**
 * Representante legal / apoderado de una persona jurídica (persona natural). Solo aplica cuando
 * el actor es jurídico (NIT). Se captura manualmente o se autopobla desde el RUNT y viaja embebido
 * en actor.metadata (sin columnas nuevas). No es un actor de primera clase.
 */
export interface RepresentanteLegal {
  tipoDocumento?: ActorDocumentType;
  numeroDocumento?: string;
  nombreCompleto?: string;
  email?: string;
  telefono?: string;
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
  /** Representante legal (solo persona jurídica). Embebido en actor.metadata. */
  representanteLegal?: RepresentanteLegal;
  /**
   * @deprecated HU #10956 revierte el check de consentimiento Habeas Data de HU #10885: el
   * formulario ya NO ofrece esta opción (la identidad de un actor se consulta SIEMPRE en vivo,
   * ver ADR-0031 actualizado). El campo se mantiene tipado solo porque el backend puede devolver
   * actores persistidos ANTES de esta HU con un valor previo en `GET actors`; `normalizeActors`
   * lo descarta explícitamente antes de cada guardado — nunca vuelve a viajar en el PUT.
   */
  autorizaReutilizacionDatos?: boolean;
}

// ── Precarga de datos de CONTACTO ya conocidos (HU #10956, revierte parcialmente HU #10885) ──────
// GET /api/v1/tramites/actors/contact-lookup?tipoDocumento=..&numeroDocumento=..  (header
// X-Tenant-Id). Se dispara tras resolver la IDENTIDAD del actor en vivo (RUNT/RUES/directorio):
// NUNCA incluye nombre ni documento (esos siempre vienen de esa consulta) ni requiere consentimiento
// previo — el contacto es un dato que la propia compañía ya capturó, no una consulta a un tercero.
// Sin antecedentes, responde 200 con los 4 campos en null (AC4), nunca 404.
export interface ActorContactLookupInput {
  tipoDocumento: ActorDocumentType;
  numeroDocumento: string;
}

export interface ActorContactLookupResult {
  ciudad: string | null;
  email: string | null;
  direccion: string | null;
  telefono: string | null;
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
  // HU #10885 (Feature #10862, CF-04) — 'cache' cuando el dato se reutilizó de una consulta previa
  // vigente del mismo tenant (AC1, ADR-0030/ADR-0031), sin llamar al proveedor externo. El backend
  // NO expone `queriedAt` para este lookup (gap de contrato documentado, HU #10885): solo el origen.
  mode: 'real' | 'mock' | 'cache';
  // Campos enriquecidos (presentes cuando found=true)
  citizenStatus?: string | null;    // Estado del ciudadano (ACTIVA/INACTIVA)
  hasPendingFines?: boolean;        // true si tieneMultas == "SI"
  nroPazYSalvo?: string | null;     // Número del paz y salvo
  hasActiveLicense?: boolean;       // true si tiene al menos 1 licencia ACTIVA
  licenseCategories?: string | null; // "B1" o "B1,C1"
  // Detalle de comparendos del SIMIT (best-effort), presente cuando hasPendingFines=true y el SIMIT
  // respondió. El RUNT conductor solo trae el flag; el detalle viene del SIMIT del mismo documento.
  fines?: FineDetail[] | null;
}

// HU #10611 (Feature #10587) — validación en línea del SOAT (re-consulta RUNT) en estado 'asignado'.
export type SoatEstado = 'vigente' | 'vencido' | 'unknown';
export interface ValidateSoatResult {
  vigente: boolean;
  soatEstado: SoatEstado;
  vencimiento: string | null;
  aseguradora: string | null;
  message: string;
}

// ── Autopopulado JURÍDICO desde RUES (persona jurídica / NIT) ───────
// POST /instances/{id}/rues-lookup  body { documentNumber }
// Bifurcación del "Consultar RUNT" cuando el actor es persona jurídica. Siempre 200 ante una
// petición válida; `found` indica si RUES halló la empresa. Si no, el usuario completa la razón
// social manualmente (fallback).
export interface RuesPersonLookupInput {
  documentNumber: string;
}

export interface RuesPersonLookupResult {
  found: boolean;
  razonSocial: string | null;
  estado: string | null;             // ACTIVA / INACTIVA / …
  documentNumber: string;
  matriculaMercantil: string | null;
  camaraComercio: string | null;
  documentType: 'NIT';
  source: 'RUES';
  // HU #10885 — igual que RuntPersonLookupResult.mode: 'cache' = dato reutilizado (AC1), sin
  // `queriedAt` disponible en el backend para este lookup.
  mode: 'real' | 'mock' | 'cache';
}

// ── Directorio de representantes/escrituras — consumo del wizard (HU #10903/#10906) ──
// GET /api/v1/tramites/deeds/active (tenant-scoped por header). Cada fila es el par (escritura ×
// compañía representada) de una escritura activa y VIGENTE del tenant, proyectada para el collapse
// del primer paso del wizard: NIT + razón social + días restantes de vigencia. Una misma compañía
// (NIT) puede aparecer en varias filas si tiene más de una escritura vigente (Feature #10929); `id`
// (de la escritura) y `description` distinguen esas filas.
export interface ActiveDeed {
  /** Id de la escritura (llave estable de la fila; distingue dos escrituras del mismo NIT). */
  id: string;
  nit: string;
  name: string;
  diasRestantes: number;
  /** Vigencia hasta (fecha ISO YYYY-MM-DD). */
  vigenciaHasta: string;
  /** Descripción de la escritura (p. ej. número/notaría), si viene. */
  description?: string | null;
}

/** Compañía representada precargada por NIT (razón social + contacto). */
export interface LegalRepresentativeLookupCompany {
  nit: string;
  razonSocial: string;
  email?: string | null;
  address?: string | null;
  city?: string | null;
  phone?: string | null;
}

/** Representante legal (persona natural) precargado por NIT. */
export interface LegalRepresentativeLookupContact {
  tipoDoc: string;
  documento: string;
  nombres: string;
  primerApellido: string;
  segundoApellido?: string | null;
  email?: string | null;
  telefono?: string | null;
}

/**
 * Un representante seleccionable de la compañía (HU #10937), con sus banderas de firma/identidad
 * vigentes calculadas por su propio documento. Cuando la compañía tiene VARIOS, el FE muestra un
 * selector; el elegido precarga sus datos y firma con su información (firma del baúl o validación de
 * identidad por su documento). `documento` es PII (Ley 1581): no loguear.
 */
export interface LegalRepresentativeOption {
  tipoDoc: string;
  documento: string;
  nombres: string;
  primerApellido: string;
  segundoApellido?: string | null;
  email?: string | null;
  telefono?: string | null;
  firmaVigente: boolean;
  identidadVigente: boolean;
}

// GET /api/v1/tramites/legal-representatives/lookup?nit=NNN — precarga comprador/vendedor por NIT.
// 200 con el match (compañía + representante(s) + banderas de firma/identidad VIGENTES al momento) o
// 404 → null (el FE cae a la consulta RUES/RUNT normal). HU #10937: `representantes` trae TODOS los
// representantes activos de la compañía para el selector; `representante`/`firmaVigente`/
// `identidadVigente` reflejan el primario (primero) por compatibilidad con el consumo previo.
export interface LegalRepresentativeLookupResult {
  company: LegalRepresentativeLookupCompany;
  representante: LegalRepresentativeLookupContact;
  firmaVigente: boolean;
  identidadVigente: boolean;
  /** HU #10937 — todos los representantes activos de la compañía (para elegir cuál firma). */
  representantes: LegalRepresentativeOption[];
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

/**
 * Detalle de un comparendo/multa pendiente, para listarlo bajo la advertencia de multas del
 * pre-vuelo. Todos los campos son opcionales (cada fuente expone lo que trae). Nunca lleva datos del
 * infractor (Habeas Data): solo información del comparendo.
 */
export interface FineDetail {
  numero?: string | null;
  fecha?: string | null;
  valor?: number | null;
  organismo?: string | null;
  estado?: string | null;
  infraccion?: string | null;
}

export interface PreflightCheck {
  key: string;
  label: string;
  status: PreflightCheckStatus;
  source: string;
  message: string;
  action?: PreflightAction | null;
  /** Detalle line-by-line del hallazgo (hoy: los comparendos de un check de multas). */
  details?: FineDetail[] | null;
}

export interface PreflightSnapshot {
  overall: PreflightOverall;
  checks: PreflightCheck[];
  createdAt: string;
  /**
   * HU #10885 (Feature #10862, CF-04, AC1) — presentes solo cuando el snapshot viene de
   * `tramitesClient.runConsultation` (espejo de `ConsultationResult.fromCache/queriedAt`, ADR-0030).
   * `runPreflight`/`getPreflight` (semáforo multi-proveedor) no los completan hoy: quedan
   * `undefined` y el panel simplemente no muestra el badge de origen/fecha.
   */
  fromCache?: boolean;
  queriedAt?: string | null;
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
  /**
   * HU #10878/#10885 (ADR-0030, CF-04) — `true` cuando el resultado se sirvió desde
   * `tramites.external_query_cache` (AC1), sin llamar al proveedor externo. `queriedAt` es la
   * fecha de la consulta ORIGEN (la que generó el dato cacheado, no necesariamente "ahora").
   */
  fromCache?: boolean;
  queriedAt?: string | null;
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

/**
 * HU #10975 (Feature #10972) — resultado de persistir en `field_values` lo que extrajo el OCR.
 * Las dos listas de omitidos son deliberadas: sin ellas, "el certificado sigue saliendo vacío"
 * no se puede depurar desde fuera del backend.
 */
export interface PersistOcrFieldsResult {
  /** Cuántas llaves se escribieron efectivamente. */
  persistidos: number;
  /** Llaves que ya tenían un valor de mayor precedencia (consulta al RUNT o dato del usuario). */
  omitidosPorPrecedencia: string[];
  /** Campos del OCR que no están en la whitelist del tipo de documento. */
  ignoradosFueraDeAlcance: string[];
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
  /**
   * FEATURE 05 — `true` si el RNMC aplica a este trámite (el OT destino lo exige y la compañía no lo
   * inhabilitó para ese OT). Solo entonces el formulario de actores muestra la fecha de expedición del
   * documento (se consulta y se genera el certificado). Ausente/false ⇒ se oculta.
   */
  rnmcEnabled?: boolean;
  /**
   * HU #10879/#10883 — paso actual PERSISTIDO (autosave del avance del wizard, PATCH
   * /instances/{id}/current-step). Si NO es null/ausente, PRIMA como punto de retoma al reabrir el
   * borrador (AC2 de HU #10883): el frontend abre en esta `key` de paso. Si es null, el frontend cae
   * al paso DERIVADO de los gates (comportamiento previo, sin regresión).
   */
  persistedCurrentStep?: string | null;
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
  // Feature #10707 — trazabilidad del avalúo (opcional; el back las persiste).
  valueOrigin?: 'suggestion' | 'manual' | null;
  suggestedSource?: string | null;
  suggestedValue?: number | null;
}

// ── Avalúo comercial (Feature #10707) — GET /commercial/suggested-value ──

export type AvaluoSourceKey = 'fasecolda' | 'base_gravable' | 'mercado_libre';
export type AvaluoStatus = 'ok' | 'no_data' | 'error';

export interface AvaluoSource {
  source: AvaluoSourceKey | string;
  status: AvaluoStatus | string;
  value: number | null;
  currency: string;
  message: string | null;
  muestras: number | null;
}

export interface SuggestedCommercialValue {
  sugerido: number | null;
  fuentePrincipal: string | null;
  sources: AvaluoSource[];
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
 *
 * HU #10869 — Feature #10864: instanceId, referenceNumber y modalidad son nullable para soportar
 * prevalidaciones standalone (sin trámite asociado). Los campos null se muestran como "—" en la
 * tabla y la navegación al trámite se condiciona a instanceId != null.
 */
export interface TenantBiometricValidation {
  id: string;
  /** HU #10869 — null para prevalidaciones standalone (sin trámite). */
  instanceId: string | null;
  /** HU #10869 — null para prevalidaciones standalone (sin trámite). */
  referenceNumber: string | null;
  /** HU #10869 — null para prevalidaciones standalone (sin trámite). */
  modalidad: string | null;
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
  /**
   * CF-05 (HU #10886, AC2) — enlace de captura VIGENTE, para reenviarlo por otros medios. Null cuando
   * no hay nada que compartir: proveedor sin enlace (mock), estado terminal o enlace ya vencido.
   */
  captureUrl: string | null;
  /** Vencimiento del ENLACE de captura (distinto de `validUntil`, que es la vigencia de la identidad). */
  linkExpiresAt: string | null;
  /**
   * CF-05 (Feature #11004, HU #11006) — correo de la validación, vista autenticada del gestor del
   * tenant (D3): completo, sin enmascarar. `null` si el backend aún no lo envía (HU #11005 en curso
   * en paralelo) — se muestra "—" sin romper la tabla.
   */
  email: string | null;
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
  /**
   * CF-02 (Feature #11004, HU #11006) — true = solo prevalidaciones standalone (sin trámite);
   * false = solo ligadas a trámite; omitido = todas (comportamiento de Validaciones — CF-03).
   */
  standalone?: boolean;
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

/**
 * Categoría de alerta ACCIONABLE de una validación de identidad (HU #10873/#10875). Espejo de
 * `IdentityValidationAlertKinds` del backend. `null` (fuera de este union) = sin alerta, solo puede
 * traer recordatorio de reenvío (`RequiresResendReminder`).
 */
export type IdentityValidationAlertKind = 'rechazada' | 'expirada' | 'por_vencer' | 'atascada';

/**
 * Fila de alerta/recordatorio de validación de identidad (HU #10873, AC1/AC2). Espejo de
 * `IdentityValidationAlertDto`. Consumida por la vista consolidada del trámite (HU #10875, POR PULL —
 * sin campana ni push).
 */
export interface IdentityValidationAlert {
  id: string;
  /** null en prevalidaciones standalone (Feature #10864): la validación no cuelga de ningún trámite. */
  instanceId: string | null;
  referenceNumber: string;
  recipientUserId: string;
  // string (no BiometricParte): el DTO del backend no acota el rol a comprador/vendedor — futuro-proof.
  partyRole: string | null;
  name: string;
  documentType: string;
  documentNumber: string;
  status: BiometricEstado;
  alertKind: IdentityValidationAlertKind | null;
  requiresResendReminder: boolean;
  daysRemainingVigencia: number | null;
  expiresAt: string | null;
  createdAt: string;
}

/** Respuesta de GET .../identity-validation/alerts (tenant o por instancia). Espejo de IdentityValidationAlertsResponse. */
export interface IdentityValidationAlertsResponse {
  alerts: IdentityValidationAlert[];
  total: number;
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
  | 'firma_baul'           // HU #10646 — actor jurídico (NIT) con firma electrónica vigente en el baúl:
                           // la identidad queda satisfecha server-side, sin biométrica (validationId null)
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

/**
 * Respuesta de GET /instances/{id}/fur/template-format (HU #10924): plantilla de FUR que aplica según
 * la clasificación del vehículo. `format` ∈ 'AUTOMOTOR' | 'MAQUINARIA' | 'REMOLQUES'.
 */
export interface FurTemplateFormatResult {
  format: string;
  vehicleClass: string | null;
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

// ── Prevalidación de identidad standalone (Feature #10864 — HU #10868) ──────
// POST /api/v1/tramites/biometric-validations (sin instanceId)
// Crea una validación biométrica sin trámite previo. El enlace de captura
// se devuelve en captureUrl para que el operador lo comparta.

/**
 * Tipo de persona para prevalidación standalone. 'natural' → valida al titular;
 * 'juridical' → valida al representante legal (datos legalRep* requeridos).
 */
export type PrevalidacionPersonType = 'natural' | 'juridical';

/**
 * Cuerpo del POST /api/v1/tramites/biometric-validations (sin trámite).
 * Espejo de IniciarPrevalidacionRequest del contrato OpenAPI (§5.2 del diseño).
 */
export interface IniciarPrevalidacionRequest {
  documentType: string;
  documentNumber: string;
  name: string;
  email: string;
  personType?: PrevalidacionPersonType;
  legalRepDocumentType?: string | null;
  legalRepDocumentNumber?: string | null;
  legalRepName?: string | null;
  legalRepEmail?: string | null;
}

/**
 * Resultado del POST /api/v1/tramites/biometric-validations (201/202).
 * Espejo de IniciarKyverumVerifyResult del contrato OpenAPI (§5.1 del diseño).
 */
export interface IniciarPrevalidacionResult {
  validationId: string;
  captureUrl: string | null;
  status: BiometricEstado;
  /** 201 = creada de inmediato; 202 = encolada (fallo transitorio del proveedor). */
  enqueued?: boolean;
}

// ── HU #10944 (Feature #10864, CF-03) — editar y reenviar prevalidación ─────

/**
 * Cuerpo del PATCH /api/v1/tramites/biometric-validations/{id} — HU #10943/#10944, D7.
 * Todos los campos son opcionales: solo se envían los que el operador cambió. El tipo/número
 * de documento (titular o RL) NUNCA se envían desde esta pantalla — no son editables (D7); el
 * backend los acepta solo para DETECTAR un intento de cambio (422 documento_no_editable).
 */
export interface EditarPrevalidacionRequest {
  name?: string;
  email?: string;
  legalRepName?: string;
  legalRepEmail?: string;
}

/**
 * Resultado del PATCH — espejo de EditarPrevalidacionResult del contrato OpenAPI.
 * `resent=true` ⟺ el cambio de correo disparó el reenvío automático (D8); `captureUrl` solo viene
 * poblado si `resent=true` y el envío no quedó encolado (fallo transitorio del proveedor).
 */
export interface EditarPrevalidacionResult {
  validation: BiometricValidation;
  captureUrl: string | null;
  resent: boolean;
}

/**
 * Resultado del POST .../resend (reenvío manual) — espejo de ReenviarPrevalidacionResult del
 * contrato OpenAPI. `queued=true` (HTTP 202) ⟺ el proveedor falló transitoriamente; el reenvío
 * YA consumió cupo del tope (D10) aunque el envío no se completó.
 */
export interface ReenviarPrevalidacionResult {
  validation: BiometricValidation;
  captureUrl: string;
  queued?: boolean;
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

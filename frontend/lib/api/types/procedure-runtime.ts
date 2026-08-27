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
export type PlateFlowStatus = 'preasignado' | 'asignado' | 'terminado';

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
  /** Familia del trámite; gobierna el bloqueo por compañía. El nombre del campo es heredado. */
  modalidad: ProcedureFamily | WizardModalidad;
  /**
   * ADR-0050 — `code` del tipo elegido en el catálogo. Manda sobre `modalidad`: decide qué
   * identificador exige la consulta y qué trámite se crea. Sin él, todo lo que no fuera traspaso se
   * consultaba y creaba como matrícula inicial.
   */
  procedureTypeCode?: string | null;
  vin?: string | null;
  plate?: string | null;
  ownerDocumentType?: string | null;
  ownerDocumentNumber?: string | null;
  /**
   * HU #11199 — secretaría de tránsito elegida en el primer paso. Obligatoria en matrícula inicial
   * (sin ella el backend no consulta el VIN); en traspaso va nula, porque el organismo lo impone el
   * RUNT según dónde esté matriculado el vehículo.
   */
  transitOfficeId?: string | null;
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

/**
 * Respuesta de POST /instances/{id}/plate-flow/complete. El trámite avanzó a Terminado, pero puede
 * hacerlo con salvedades: p. ej. la compañía permite continuar sin SOAT vigente
 * (`warningCode = 'soat_no_vigente_advertencia'`). La UI debe mostrar `warningMessage` aunque la
 * llamada haya sido exitosa.
 */
export interface CompletePlateFlowResult {
  instance: ProcedureInstanceSummary | null;
  warningCode: string | null;
  warningMessage: string | null;
}

// ── Listado de instancias (Slice M6) ───────────────────────────────
// Contrato FIJO acordado con backend:
//   GET /api/v1/tramites/instances  (X-Tenant-Id)  -> { items: InstanceSummary[] }
// Resumen pensado para la tabla de "Trámites en curso" de OperacionView:
// trae placa/VIN/vehículo/comprador desnormalizados + progreso del wizard.
export interface InstanceSummary {
  id: string;
  referenceNumber: string;
  /**
   * ADR-0050 — FAMILIA del tipo de trámite (`MATRICULAS` | `TRASPASO` | `OTROS`). Conserva el
   * nombre `modalidad` porque así viaja en el contrato del listado; lo que cambió es su contenido,
   * que antes era una de las dos modalidades de entrada.
   */
  modalidad: ProcedureFamily;
  /**
   * ADR-0050 — nombre del TIPO en el catálogo («Blindaje», «Cambio de color», «Levantamiento de
   * prenda»…). La familia sola identifica bien una matrícula o un traspaso, pero agrupa quince tipos
   * bajo «Otros»: sin esto, tres trámites distintos se ven idénticos en el listado.
   * Ausente en expedientes servidos por un backend anterior a este campo.
   */
  tipoNombre?: string | null;
  /** `code` canónico del tipo, para decidir por tipo sin depender del nombre mostrado. */
  tipoCodigo?: string | null;
  estado: InstanceStatus;
  /** Feature #10587 / HU #10785 — sub-estado interno de placa (null | preasignado | asignado). */
  plateFlowStatus?: PlateFlowStatus | null;
  placa: string | null;
  vin: string | null;
  vehiculoMarca: string | null;
  vehiculoLinea: string | null;
  compradorNombre: string | null;
  compradorDocumento: string | null;
  /**
   * HU #11020 — parte SALIENTE del traspaso, para identificar el trámite desde el dashboard sin
   * abrirlo. `null` en matrícula inicial (no hay vendedor).
   */
  vendedorNombre?: string | null;
  vendedorDocumento?: string | null;
  organismoTransito: string | null;
  pasoActual: number;
  totalPasos: number;
  /**
   * Rótulo del paso en curso, tomado del recorrido del TIPO. Antes el cliente lo derivaba de una
   * lista de nombres por familia: para OTROS estaba vacía —salía «—»— y de todos modos no puede
   * acertar, porque desde ADR-0050 cada tipo tiene su propio recorrido.
   * Ausente si el tipo no está parametrizado o el backend es anterior al campo.
   */
  pasoNombre?: string | null;
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
  /** Subsanación activa sobre rechazado (edición sin cambiar status). */
  subsanacionActiva?: boolean;
  /** Veces que se activó la subsanación en este expediente. */
  subsanacionCount?: number;
  /** Motivo (texto libre) del último rechazo del OT; null si no hay rechazo con motivo. */
  ultimoRechazoMotivo?: string | null;
  // ── HU #11056 — columnas de seguimiento del listado ──────────────────────────────
  /** Última modificación del trámite; null si no se ha modificado desde que se creó. */
  updatedAt?: string | null;
  /** Persona que radica (nombre visible del usuario creador); null si no se pudo resolver. */
  gestorNombre?: string | null;
  /** Fuente por la que entró el trámite. Ver `TramiteFuente` en backend. */
  fuente?: TramiteFuente | null;
  /**
   * Cómo queda ACREDITADA cada parte: por validación de identidad o por firma del baúl. `null` = no
   * aplica (el vendedor no existe en matrícula inicial).
   */
  firmaVendedorEstado?: FirmaParteEstado | null;
  firmaCompradorEstado?: FirmaParteEstado | null;
  /**
   * HU #11055 — adjunto del expediente consolidado del gestor, si ya está generado. `null` = no
   * generado ⇒ la fila NO ofrece la acción (el botón no dispara generación).
   */
  consolidadoAttachmentId?: string | null;
  // ── ICT (PR #204) — pausa de trámites de la integración ──────────────────────────
  /**
   * ICT (servicio v1 pauseDraftProcess / bandera starts_procedure_in_paused): el trámite está pausado
   * y no avanza (la radicación se bloquea) hasta reanudarlo. Default false para trámites de plataforma.
   */
  isPaused?: boolean;
  /** Nota informativa mostrada cuando el trámite está pausado (origen ICT). null si no está pausado. */
  pausedObservation?: string | null;
  /**
   * Origen del trámite: 'ict' para los creados por la integración con terceros. Solo esos ofrecen la
   * acción de pausar/reanudar en la UI (paridad v1). null/'' para trámites de plataforma.
   */
  origin?: string | null;
}

/** Fuente por la que el trámite entró a FLIT (HU #11056). No existe fuente "QX": Quipux es salida. */
export type TramiteFuente = 'dashboard' | 'integracion' | 'migrado';

/**
 * Cómo queda acreditada una parte en el listado. Son los ÚNICOS tres estados válidos:
 * - `pendiente`: la validación de identidad no se ha realizado (y no hay firma del baúl).
 * - `firmado`: identidad validada y aprobada, o firma del baúl vigente.
 * - `rechazado`: identidad rechazada, o firma del baúl vencida.
 *
 * No habla de la firma electrónica de la compraventa: ese es otro eje (`signaturePending`).
 */
export type FirmaParteEstado = 'pendiente' | 'firmado' | 'rechazado';

/**
 * Query params de GET /api/v1/tramites/instances (filtros + orden server-side).
 * Si no se envía ninguno, el backend responde el TOP-N legacy sin `total`.
 */
export interface ListInstancesParams {
  /** SuperAdmin: acota el listado a una compañía (header X-Tenant-Id). */
  filterTenantId?: string;
  vin?: string;
  placa?: string;
  /** Subcadena sobre el nombre del propietario/vendedor. */
  vendedor?: string;
  comprador?: string;
  gestor?: string;
  /**
   * Firma electrónica de la compraventa completa (`true`) o pendiente (`false`).
   * No es el chip de identidad/baúl de la columna de actores.
   */
  firmado?: boolean;
  /** ISO-8601 / fecha `YYYY-MM-DD` (el cliente normaliza a inicio/fin de día). */
  createdFrom?: string;
  createdTo?: string;
  updatedFrom?: string;
  updatedTo?: string;
  /** Whitelist backend: vin | placa | comprador | gestor | createdAt | updatedAt */
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  skip?: number;
  take?: number;
}

/** Respuesta de GET /instances. `total` solo viene en el camino filtrado/ordenado. */
export interface InstancesResponse {
  items: InstanceSummary[];
  total?: number;
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

/**
 * Tipo de servicio del vehículo — catálogo cerrado `catalogs.vehicle_service_types` (sección 18 del
 * FUR). Seis valores fijos (PARTICULAR/PUBLICO/DIPLOMATICO/OFICIAL/ESPECIAL/OTROS); `sortOrder`
 * respeta el orden normativo del FUR con el que el backend ya lo devuelve.
 */
export interface VehicleServiceTypeOption {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
}

export interface VehicleServiceTypesResponse {
  items: VehicleServiceTypeOption[];
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
  /**
   * HU #11014 — correo del actor. Respaldo del expediente cuando la identidad está apalancada o
   * cubierta por el baúl y no hay validación propia de la que leerlo. PII (Ley 1581).
   */
  email?: string | null;
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
  /** Subsanación activa sobre rechazado (edición sin cambiar status). */
  subsanacionActiva?: boolean;
  /** Veces que se activó la subsanación en este expediente. */
  subsanacionCount?: number;
  /**
   * HU #10536 — marca de prioridad. Vive en una columna del expediente, no en `fieldValues`, así que
   * es el único dato del paso 1 que no se puede releer desde ahí al volver sobre un trámite creado.
   */
  prioritario?: boolean;
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

/**
 * Rol de la parte en el trámite.
 *
 * `locatario` es el arrendatario del leasing (`LESSEE`). Se identifica y recibe los correos del
 * trámite, pero NO valida identidad ni firma — eso es del propietario, y por eso no está en
 * {@link BiometricParte}.
 */
export type ActorRol = 'comprador' | 'vendedor' | 'locatario';

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
  // FEATURE 02 — legado: espejo de onlyOwnVehiclesByFamily.traspaso.
  onlyOwnVehicles: boolean;
  /** Solo vehículos propios por familia de trámite. */
  onlyOwnVehiclesByFamily?: {
    matriculas: boolean;
    traspaso: boolean;
    otros: boolean;
  };
  /** Bloqueo de creación por familia (`true` = no permitir crear). */
  blockProcedureFamily?: {
    matriculas: boolean;
    traspaso: boolean;
    otros: boolean;
  };
}

/**
 * Representante legal / apoderado de una persona jurídica (persona natural). Solo aplica cuando
 * el actor es jurídico (NIT). Se captura manualmente o se autopobla desde el RUNT y viaja embebido
 * en actor.metadata (sin columnas nuevas). No es un actor de primera clase.
 */
/**
 * HU #11061 — mecanismo con el que se plasma la firma del representante legal. `'baul'` = firma
 * precargada del baúl; `'identidad'` = sello de la validación biométrica.
 */
export type MecanismoFirma = 'baul' | 'identidad';

export interface RepresentanteLegal {
  tipoDocumento?: ActorDocumentType;
  numeroDocumento?: string;
  nombreCompleto?: string;
  email?: string;
  telefono?: string;
  /**
   * HU #11061 — mecanismo de firma ELEGIDO cuando el representante tiene el baúl y la identidad
   * vigentes a la vez. Ausente = sin elección explícita ⇒ el backend aplica la precedencia del baúl
   * (HU #11031), que es el comportamiento previo.
   */
  mecanismoFirma?: MecanismoFirma;
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
  // El nombre llega desglosado: `firstName` es el PRIMER nombre (no todos los de pila) y
  // `lastName` conserva los dos apellidos juntos. El RUNT enmascara sus campos de display, así
  // que el backend resuelve la separación y el front no debe volver a partir `fullName`.
  firstName: string | null;
  lastName: string | null;
  secondName?: string | null;
  firstLastName?: string | null;
  secondLastName?: string | null;
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

// ── Consulta RUES SIN trámite (paso 1, empresa vinculadora del tipo de servicio PÚBLICO) ──
// POST /api/v1/tramites/rues-preview  body { documentNumber }
// No está anclada a una instancia (el paso 1 puede correr sin trámite creado, CF-02): a diferencia
// de `ruesPersonLookup`, siempre viaja el NIT en el body y nunca un instanceId en la ruta.
export interface RuesPreviewInput {
  documentNumber: string;
}

export interface RuesPreviewResult {
  /** El proveedor respondió y no existe ese NIT en el RUES (distinto de un fallo transitorio 503). */
  found: boolean;
  nit: string;
  razonSocial: string | null;
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
  /** Id del RL que asoció la escritura; null en escrituras legadas. */
  representativeId?: string | null;
  /** Nombre completo del RL. */
  representativeName?: string | null;
  /** Tipo de documento del RL (CC, CE, …). */
  representativeDocumentType?: string | null;
  /** Número de documento del RL (PII). */
  representativeDocumentNumber?: string | null;
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
  razonSocial?: string | null;
  companyEmail?: string | null;
  companyAddress?: string | null;
  companyCity?: string | null;
  companyPhone?: string | null;
}

// GET /api/v1/tramites/legal-representatives/lookup?nit=NNN — datos básicos de empresa y RL
// para el wizard. La razón social del actor jurídico sale de RUES (ruesPersonLookup), no de aquí.
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
  /**
   * Origen del adjunto. NO es un catálogo cerrado (el backend puede sumar valores sin romper el
   * contrato): usar {@link ATTACHMENT_SOURCE_LABELS} para la etiqueta, con fallback al valor crudo.
   * `'company'` (Feature #11309/#11313, ADR-0042) — versión activa de un documento personalizado de
   * la compañía (mandato | tramite_virtual), sustituida en el único punto del pipeline de
   * generación. Se distingue así de `'system'` (generado por FLIT) y de `'user'`/`'ot'` (cargado por
   * una persona).
   */
  source: string;
  uploadedAt: string;
}

/**
 * Etiqueta legible del origen de un adjunto (HU #11315). Un valor no listado aquí no es un error: se
 * muestra su texto crudo (`source`) en vez de asumir el conjunto cerrado — el backend no promete una
 * lista fija.
 */
export const ATTACHMENT_SOURCE_LABELS: Partial<Record<string, string>> = {
  system: 'Generado por FLIT',
  company: 'Documento de la compañía',
  user: 'Cargado por el usuario',
  ot: 'Cargado por el organismo',
  ocr: 'Cargado (OCR)',
  portal: 'Cargado desde el portal',
  consultation: 'Consulta automática',
  ict: 'Integración (ICT)',
};

/** Etiqueta de un `source` de adjunto, con fallback al valor crudo si no está en el mapa. */
export function attachmentSourceLabel(source: string): string {
  return ATTACHMENT_SOURCE_LABELS[source] ?? source;
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
 * Una pieza propuesta por el cargue masivo: un documento que el clasificador reconoció dentro de un
 * archivo, ya recortado y verificado con el prompt de su tipo. NO está subida — vive en la pantalla de
 * revisión hasta que el operador la confirma.
 */
export interface BatchOcrPiece {
  /** Tipo de documento al que se propone asignarla. */
  tipo: string;
  /** Archivo del lote del que salió, para que el operador se ubique. */
  sourceFilename: string;
  /** Nombre propuesto del adjunto (`soat_expediente.pdf` cuando hubo recorte). */
  filename: string;
  mimetype: string;
  sizeBytes: number;
  /** Páginas del archivo original que ocupa, base 1. */
  paginas: number[];
  /** Páginas del archivo original, para el chip «recorte 3/16 págs». */
  totalPaginasOrigen: number;
  /** Certeza del clasificador, 0.0–1.0. */
  confianza: number;
  /** Por qué el clasificador la reconoció así. */
  motivo?: string | null;
  /**
   * JSON del prompt por tipo — el MISMO que devuelve el cargue campo a campo, así que se evalúa con
   * `evaluateOcr` y se pinta con `OcrStatusPanel` sin duplicar reglas. null si el análisis falló.
   */
  data: Record<string, unknown> | null;
  /** Por qué no hay `data`; null si el análisis fue bien. */
  analisisError?: string | null;
  /** Bytes de la pieza recortada, listos para subir al confirmar. */
  contentBase64: string;
}

/**
 * Páginas que el clasificador no supo ubicar en ningún tipo. Sin binario a propósito: el cliente
 * todavía tiene el archivo original, y la salida que se le ofrece al operador es cargarlo a mano en un
 * campo (donde el OCR dirigido reintenta la extracción) o descartarlo.
 */
export interface BatchOcrUnrecognized {
  sourceFilename: string;
  paginas: number[];
  totalPaginas: number;
}

/** Archivo del lote que no se pudo procesar, con el motivo en lenguaje del operador. */
export interface BatchOcrFileError {
  filename: string;
  motivo: string;
}

/**
 * Respuesta de POST /tramites/ocr/lote. Las tres listas son la pantalla de revisión: lo que se propone
 * subir, lo que sobró, y lo que ni siquiera se pudo abrir. Un lote donde todo falla sigue siendo un 200
 * con `piezas` vacío — el error por archivo es información para el operador, no un fallo de la petición.
 */
export interface BatchOcrResult {
  piezas: BatchOcrPiece[];
  noReconocidos: BatchOcrUnrecognized[];
  errores: BatchOcrFileError[];
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

/** Ítem de la guía informativa de documentos (paso 1, sin checklist de carga). */
export interface DocumentoInformativoPreviewItem {
  documentTypeId: string;
  codigo: string;
  nombre: string;
  obligatorio: boolean;
  orden: number;
  descripcion?: string | null;
}

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

/**
 * Renderer de una sección del paso (CFD-09). Catálogo CERRADO: espeja el CHECK de
 * `tramites.procedure_sections.section_type` y las ramas de `DynamicGateEvaluator`. Añadir un valor
 * exige PR coordinado backend + frontend + migración.
 */
export type WizardSectionType =
  | 'vehicle_query'
  | 'document_checklist'
  | 'actor_form'
  | 'commercial'
  | 'biometric'
  | 'signature_fur'
  | 'plate_request'
  | 'prenda_decision'
  | 'generic_form';

export interface WizardStep {
  index: number;
  key: WizardStepKey | string;
  label: string;
  status: WizardStepStatus;
  /** Códigos de razón de incompletitud (mapeados a copy en la UI). */
  reasons: string[];
  /**
   * ADR-0050 / CFD-09 — renderer principal del paso, decidido por la parametrización del tipo y no
   * por su clave. Es lo que permite que un trámite de OTROS tenga recorrido propio sin que el
   * cliente conozca su `key`.
   */
  sectionType?: WizardSectionType;
  /** Todas las secciones del paso, en orden. Un paso puede tener más de una. */
  sectionTypes?: WizardSectionType[];
  /**
   * Capacidades del tipo que la sección necesita para pintarse (entryMode, actores, firma…).
   *
   * ADR-0051 — la sección `actor_form` trae `revealSellerForm?: boolean`: señal POR INSTANCIA (no
   * de tipo) que excepciona `sellerCapturedViaForm:false` cuando el vendedor sincronizado quedó sin
   * un dato que el backend necesita para poder enviarle la validación de identidad (persona jurídica
   * sin representante legal resoluble, o persona natural sin correo). El backend ya calculó la
   * excepción — el cliente solo la lee, no la recalcula.
   */
  sectionConfig?: Record<string, unknown> | null;
}

/** Respuesta de GET /instances/{id}/wizard. */
/**
 * Capacidades del tipo con el que se conformó el expediente (ADR-0050).
 *
 * Es lo que le faltaba al asistente para dejar de decidir por modalidad: qué partes pide el trámite,
 * si lleva datos comerciales, si la prenda es una puerta y por qué identificador entra el vehículo.
 * Salen del mismo `gate_profile` que gobierna los gates del backend, congelado en el snapshot del
 * expediente, así que el asistente y el servidor no pueden discrepar.
 *
 * Es una proyección PARCIAL a propósito: lo que solo afecta a validaciones del servidor no viaja,
 * para que el frontend no pueda reimplementar un gate.
 */
export interface WizardCapabilities {
  /** `VIN` (el vehículo aún no tiene placa) o `PLATE`. */
  entryMode: string | null;
  /** Hay parte vendedora. En la familia OTROS el titular no vende. */
  requiresSeller: boolean;
  /** Hay parte compradora o titular. */
  requiresBuyer: boolean;
  /**
   * ADR-0051 — hay parte vendedora (`requiresSeller`) pero esa parte NO se captura tecleando datos
   * en el wizard: llega de otra fuente (sincronizada desde el RUNT, `TRASPASO_UNILATERAL`). Separa
   * "hay vendedor" (`requiresSeller`, sin cambio) de "el vendedor llena un formulario" — antes una
   * sola llave gobernaba ambas preguntas y por eso no podían responderse distinto para el mismo tipo.
   *
   * Ausente ⇒ `true` (todo tipo que hoy captura al vendedor por formulario sigue haciéndolo).
   */
  sellerCapturedViaForm?: boolean;
  /**
   * Interviene un arrendatario además del propietario (leasing). Parte declarativa: se identifica y
   * se le notifica, pero no valida identidad ni firma. Ausente ⇒ `false`.
   */
  requiresLessee?: boolean;
  allowsMultipleBuyer: boolean;
  requiresCommercialValue: boolean;
  requiresBiometrics: boolean;
  /** Actores a validar: `OWNER`, `BUYER`. */
  biometricActors: string[];
  /** La decisión de prenda es una puerta y no una declaración. */
  hasPrendaGate: boolean;
  /**
   * ADR-0050 — el expediente admite declarar transformaciones POR ENCIMA del tipo base (los
   * «trámites simultáneos» del art. 5.1.8). El backend lo entrega ya resuelto: la familia OTROS no
   * acumula —ahí el cambio ES el trámite— y matrícula y traspaso sí.
   *
   * Ausente ⇒ se trata como `true` (un borrador abierto antes de esta llave no debe perder los
   * simultáneos que ya tenía).
   */
  allowsComplementaryTransformations?: boolean;
  /**
   * Admite un gravamen por encima del tipo base. No se refiere a la prenda de un TIPO de prenda:
   * ahí la prenda es el trámite y su paso se pinta igual. Ausente ⇒ `true`.
   */
  allowsComplementaryPrenda?: boolean;
  /**
   * El organismo de tránsito lo ELIGE el operador entre los habilitados de su compañía, en vez de
   * imponerlo el RUNT. Dejó de deducirse de `entryMode`: un radicado de cuenta entra por placa y aun
   * así lo elige, porque el trámite consiste en llevar la cuenta a OTRO organismo.
   *
   * Ausente ⇒ se cae a `entryMode === 'VIN'`, que es el criterio anterior a esta llave.
   */
  operatorChoosesTransitOffice?: boolean;
  /**
   * El trámite DECLARA un organismo de destino además del suyo: el traslado de cuenta, que expide el
   * organismo de ORIGEN pero tiene que decir a dónde va la cuenta.
   *
   * No confundir con `operatorChoosesTransitOffice`: ahí el organismo elegido ES el del trámite (el
   * radicado de cuenta). Aquí el del trámite lo sigue imponiendo el RUNT y el destino es un dato más.
   */
  requiresDestinationTransitOffice?: boolean;
  /**
   * El trámite PIDE una placa nueva al organismo (matrícula, rematrícula, duplicado de placa). Es lo
   * que decide si la preferencia de dígito de preasignación tiene sentido: en un radicado de cuenta
   * el vehículo ya tiene placa y no hay ninguna que asignar.
   *
   * Ausente ⇒ se cae a `entryMode === 'VIN'`, que es como se decidía antes de esta llave.
   */
  requiresPlateRequest?: boolean;
  /**
   * Cómo se obtiene la impronta (`AUTO` | `MANUAL` | `OPERATOR_CHOICE`). Ausente ⇒ se puede generar
   * (también si el documento es opcional). `MANUAL` ⇒ solo carga de archivo.
   */
  improntaSource?: string | null;
}

export interface WizardState {
  /**
   * ADR-0050 — familia del tipo (`MATRICULAS` | `TRASPASO` | `OTROS`). El nombre del campo es
   * heredado; el backend escribe aquí `procedure_types.family` desde que se retiró
   * `modalidad_entrada`, así que declararlo como `WizardModalidad` era una promesa falsa: ninguna
   * comparación contra `'traspaso'` podía acertar.
   */
  modalidad: ProcedureFamily | WizardModalidad;
  tipologiaCodigo: string;
  /** Nombre del tipo del catálogo, para titular el trámite que se está haciendo. */
  typeName?: string | null;
  /** Ausente solo si el tipo no tiene pasos parametrizados (el asistente pinta el bloqueo). */
  capabilities?: WizardCapabilities | null;
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
  /** Subsanación activa sobre rechazado (edición sin cambiar status). */
  subsanacionActiva?: boolean;
  /** Veces que se activó la subsanación en este expediente. */
  subsanacionCount?: number;
  /**
   * Migración V1→V2 — el trámite viene de V1 y no se capturó paso a paso aquí, así que llega sin las
   * consultas de RUNT/SIMIT hechas (no se migran: caducan en minutos y no quedan atadas al trámite).
   * El wizard lo usa para DESTACAR la petición de correrlas, sin exponer ese porqué en la UI.
   * Ausente/false ⇒ trámite nativo de V2.
   */
  esMigrado?: boolean;
  /**
   * Compañía+OT: certificado de prenda obligatorio (default) u opcional (opt-out vigente al crear
   * el trámite). Ausente ⇒ se trata como obligatorio.
   */
  prendaDocumentRequired?: boolean;
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
  /**
   * Entidad ante la que se levantó el gravamen. Solo la captura el trámite de levantamiento de
   * prenda: es lo que su FUR declara en el párrafo 23. En traspaso y matrícula llega `null` y el
   * literal de esas modalidades no cambia.
   */
  levantamientoEntidad: string | null;
  createdAt: string;
}

/** Payload de PUT /prenda. */
export interface PrendaInput {
  decision: PrendaDecision;
  acreedorNombre?: string | null;
  acreedorDocumento?: string | null;
  levantamientoEntidad?: string | null;
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

/**
 * Proveedor de validación de identidad (espejo de BiometricProviders).
 * `migracion_v1` = identidad que ya venía validada de V1 y la migración trajo como hecho
 * consumado; no hubo captura ni proveedor externo, y solo acredita a su propio trámite.
 */
export type BiometricProvider = 'mock' | 'kyverum' | 'migracion_v1';

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
  // HU #11069 — trámite primario + otros del tenant con la misma identidad (detalle VID).
  procedureInstanceId?: string | null;
  referenceNumber?: string | null;
  modalidad?: string | null;
  linkedProcedures?: LinkedProcedureRef[] | null;
  /** Fecha de registro (historial por persona: más reciente → más antigua). */
  createdAt?: string | null;
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
  /**
   * HU #11014 (ADR-0025 §4) — partes cuya identidad queda cubierta por la FIRMA DEL BAÚL en vez de por
   * una validación biométrica. Se rotulan como «firmado desde el baúl»: no hay certificado que mostrar.
   */
  firmaBaulPartes?: string[] | null;
  /**
   * HU #11665 — por qué NO se envió la validación de identidad a una parte jurídica. Derivado al
   * vuelo por el backend (`EnvioValidacionBloqueoRules`), nunca persistido: desaparece en cuanto el
   * gestor corrige el dato. `null`/ausente cuando no hay ningún motivo que reportar.
   */
  motivosNoEnvio?: EnvioValidacionMotivo[] | null;
}

/**
 * HU #11665 — motivo tipificado de no envío, por parte (espejo de `EnvioValidacionMotivoDto`).
 *
 * `codigo` se deja como `string` a propósito: el backend puede tipificar un motivo nuevo antes de
 * que esta pantalla lo conozca y eso no debe romper el tipado ni la vista (ver
 * `presentarMotivoNoEnvio`). `informativo: true` NO es un fallo — explica una ausencia legítima
 * (la parte ya está cubierta) y la UI no debe pintarlo como bloqueo.
 */
export interface EnvioValidacionMotivo {
  /** Rol de la parte: `comprador` | `vendedor`. */
  parte: string;
  /** Código estable del motivo (`proveedor_no_envia`, `rl_sin_documento`, …). */
  codigo: string;
  informativo: boolean;
}

export interface LinkedProcedureRef {
  instanceId: string;
  referenceNumber: string;
  status: string;
  /** Modalidad del trámite (traspaso / matricula_inicial). HU #11069. */
  modalidad?: string | null;
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
  // string (no BiometricParte): el contrato declara partyRole como string libre y el backend lo expone
  // como string? — la vista transversal solo lo pinta como texto, nunca discrimina por rol.
  partyRole: string | null;
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
  /**
   * Feature #11066 — otros trámites del tenant con la misma identidad documental
   * (excluye el trámite primario `instanceId` si existe).
   */
  linkedProcedures?: LinkedProcedureRef[];
  /**
   * HU #11505 — intentos consumidos por la validación (mismo criterio de lectura que
   * `BiometricValidation.intentos`, ver PersonIdentityDetailDrawer). Opcional: el backend de esta vista
   * transversal aún no lo envía (HU #11504 en curso en paralelo); si falta, la grilla omite el contador
   * sin romper la fila.
   */
  intentos?: number;
  /** HU #11505 — tope de intentos de la validación. Opcional por el mismo motivo que `intentos`. */
  maxIntentos?: number;
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

/**
 * Fila agrupada por persona (HU #11270 / #11271, ADR-0040): estado de la más reciente + contador +
 * peor alerta. Espejo de TenantBiometricPersonDto.
 */
export interface TenantBiometricPerson {
  documentType: string;
  documentNumber: string;
  name: string;
  status: BiometricEstado;
  validationCount: number;
  worstAlertKind: IdentityValidationAlertKind | null;
  latestValidationId: string;
  instanceId: string | null;
  referenceNumber: string | null;
  modalidad: string | null;
  partyRole: string | null;
  email: string;
  provider: BiometricProvider;
  score: number | null;
  /** URL de captura de la validación más reciente (null si aún no hay enlace). */
  captureUrl: string | null;
  expired: boolean;
  createdAt: string;
  validatedAt: string | null;
  validUntil: string | null;
  daysRemaining: number | null;
  linkExpiresAt: string | null;
  /**
   * HU #11505 — intentos consumidos por la validación MÁS RECIENTE de la persona (mismo criterio de
   * lectura que `BiometricValidation.intentos`, ya usado en PersonIdentityDetailDrawer). Opcional a
   * propósito: el backend aún no los envía en esta vista agrupada (queda para la capa backend de esta
   * misma HU); si faltan, la grilla omite el contador sin romper la fila (AC4).
   */
  intentos?: number;
  /** HU #11505 — tope de intentos de la validación más reciente. Opcional por el mismo motivo. */
  maxIntentos?: number;
}

/** Respuesta de GET /biometric-validations/by-person. `total` = personas; `stats` = validaciones. */
export interface TenantBiometricPersonsResponse {
  persons: TenantBiometricPerson[];
  stats: BiometricValidationStats;
  page: number;
  pageSize: number;
  total: number;
}

/** Filtros del listado agrupado (solo semántica de persona). */
export interface TenantBiometricPersonFilters {
  name?: string;
  documentType?: string;
  documentNumber?: string;
  status?: BiometricEstado;
  createdFrom?: string;
  createdTo?: string;
  vigenciaEstado?: BiometricVigenciaEstado;
  expiraDesde?: string;
  expiraHasta?: string;
  venceEnDias?: number;
  page?: number;
  pageSize?: number;
  standalone?: boolean;
}

/**
 * Historial multi-validación por persona (HU #11272 / #11273). Espejo de
 * PersonBiometricValidationsResponse. `allTerminal` detiene el polling del drawer.
 */
export interface PersonBiometricValidationsResponse {
  documentType: string;
  documentNumber: string;
  name: string | null;
  validations: BiometricValidation[];
  page: number;
  pageSize: number;
  total: number;
  allTerminal: boolean;
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
  /**
   * HU #11017 — el consolidado ya no se bloquea por documentos obligatorios faltantes: se genera y se
   * marca. `incompleto` avisa al gestor y `documentosFaltantes` dice exactamente qué falta.
   */
  incompleto?: boolean;
  documentosFaltantes?: string[] | null;
  /**
   * HU #11050 (AC3) — documentos de la cascada que NO se pudieron generar, con su motivo
   * (`"impronta: provider_unavailable"`). Antes el fallo se descartaba en silencio y el consolidado
   * salía sin ese documento sin que el gestor supiera por qué. No bloquea: el consolidado se entrega.
   */
  avisosCascada?: string[] | null;
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

/** HU #11470 — fila de despacho de correo (dirección enmascarada). */
export interface NotificationDispatchItem {
  id: string;
  recipientRole: string;
  recipientKind: string;
  recipientMasked: string | null;
  recipientName: string | null;
  templateKey: string;
  status: string;
  failureReason: string | null;
  attempts: number;
  queuedAt: string;
  processedAt: string | null;
}

export interface NotificationDispatchesResponse {
  items: NotificationDispatchItem[];
}

/**
 * HU #11203 — un mandatario que puede firmar el mandato del trámite.
 *
 * Puede firmar por cualquiera de dos vías ALTERNATIVAS: `firmaBaulVigente` o `identidadVigente`. Antes
 * solo se informaba la identidad, así que un mandatario con su firma del baúl vigente —perfectamente
 * capaz de firmar— se anunciaba como si le faltara algo.
 */
export interface MandateSignerOption {
  id: string;
  nombre: string;
  tipoDocumento: string;
  documento: string;
  identidadVigente: boolean;
  identidadHasta: string | null;
  firmaBaulVigente?: boolean;
  /**
   * Firma A MANO ante el organismo del trámite. Quien firma a mano no necesita ninguna de las dos vías
   * anteriores: el documento le deja la línea y él la suscribe.
   */
  firmaFisica?: boolean;
}

/** Mandatarios disponibles y cuál está elegido. `editable` es falso fuera de borrador. */
export interface MandateSignerSelection {
  opciones: MandateSignerOption[];
  elegidoId: string | null;
  editable: boolean;
}

/**
 * HU #11197 - estado de la firma a posteriori de una parte. `aplica` es true solo cuando el
 * representante legal tiene la identidad Y la firma del baul vencidas: con cualquiera de las dos
 * vigente el tramite puede firmarse ya y la opcion no se ofrece.
 */
export interface FirmaPosteriorEstado {
  aplica: boolean;
  marcado: boolean;
  representanteNombre?: string | null;
  marcadoAt?: string | null;
}

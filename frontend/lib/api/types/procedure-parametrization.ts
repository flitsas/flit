import type { WizardSectionType } from './procedure-runtime';
export type PublicationStatus = 'draft' | 'published' | 'archived';
export type ProcedureFamily = 'MATRICULAS' | 'TRASPASO' | 'OTROS';
export type ProcedureEntityCode = 'VEHICLE' | 'OWNER' | 'BUYER' | 'LESSEE';
export type FieldType = 'text' | 'number' | 'select' | 'radio' | 'checkbox' | 'date';
export type ValidationErrorCode =
  | 'MISSING_CONFORMATION'
  | 'MISSING_REQUIRED_FIELD'
  | 'LOCKED_FIELD_REMOVED'
  | 'VIN_PLATE_RULE'
  | 'NIT_PERSON_TYPE'
  | 'INCOMPLETE_CONSULTATION_FIELDS';

export interface ProcedureTypeSummary {
  id: string;
  code: string;
  name: string;
  family: ProcedureFamily;
  publicationStatus: PublicationStatus;
  isActive: boolean;
  /**
   * ADR-0050 — barrera de operación: el tipo puede elegirse al crear un trámite.
   *
   * Es independiente de `publicationStatus`, que solo gobierna la visibilidad en administración: un
   * tipo puede estar publicado y aparecer en el catálogo sin tener todavía un recorrido operable.
   * El selector de creación filtra por este campo; las pantallas de administración, no.
   */
  wizardEnabled: boolean;
  publishedAt: string | null;
}

/** Etiquetas de familia para el selector y los filtros. */
export const FAMILY_LABEL: Record<ProcedureFamily, string> = {
  MATRICULAS: 'Matrículas',
  TRASPASO: 'Traspaso',
  OTROS: 'Otros trámites',
};

/** Descripción de cada familia en el paso de selección. */
export const FAMILY_DESCRIPTION: Record<ProcedureFamily, string> = {
  MATRICULAS: 'Primera matrícula, leasing y cancelación',
  TRASPASO: 'Cambio de propietario del vehículo',
  OTROS: 'Cambios y novedades sobre un vehículo ya matriculado',
};

/** Orden de presentación de las familias. */
export const FAMILY_ORDER: readonly ProcedureFamily[] = ['MATRICULAS', 'TRASPASO', 'OTROS'];

export interface ConformationRuleItem {
  procedureEntityCode: ProcedureEntityCode;
  isActive: boolean;
  sortOrder: number;
  validationProfile?: Record<string, unknown>;
}

export interface FormFieldItem {
  id?: string;
  fieldKey: string;
  label: string;
  fieldType: FieldType;
  isRequired: boolean;
  sortOrder?: number;
  isLocked: boolean;
  lockReason: string | null;
  consultationTemplateId: string | null;
  validationSchema?: Record<string, unknown> | string;
  options?: string | Record<string, unknown>;
}

export interface ProcedureSection {
  id?: string;
  code: string;
  title: string;
  sortOrder: number;
  layout?: string;
  /**
   * CFD-09 / ADR-0050 — qué pinta la sección en el asistente. Es el contrato entre la
   * parametrización y el `SectionRendererRegistry` del frontend: una sección sin tipo válido cae en
   * el cuerpo genérico y no captura nada.
   */
  sectionType?: WizardSectionType;
  formFields: FormFieldItem[];
}

export interface ProcedureStep {
  id?: string;
  code: string;
  title: string;
  sortOrder: number;
  isActive?: boolean;
  sections: ProcedureSection[];
}

export interface ValidationError {
  code: ValidationErrorCode;
  message: string;
  path: string;
}

export interface ValidationResult {
  isValid: boolean;
  errors: ValidationError[];
}

export interface ProcedureEntity {
  id: string;
  code: ProcedureEntityCode;
  name: string;
  isActive: boolean;
}

export interface ExternalDataSource {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export interface ConsultationTemplate {
  id: string;
  code: string;
  name: string;
  externalDataSourceId: string;
  entityScope: 'vehicle' | 'actor';
  personType: 'natural' | 'juridical' | null;
  requiredFieldKeys: string[];
  isActive: boolean;
}

export interface CreateProcedureTypeRequest {
  /** Llave del catálogo. No se puede cambiar después: viaja a ICT, a Quipux y a los snapshots. */
  code: string;
  name: string;
  family: ProcedureFamily;
  description?: string | null;
}

/** Payload item for PUT /procedure-types/{id}/steps (array body, not wrapped). */
export interface ProcedureStepInput {
  code: string;
  title: string;
  sortOrder: number;
  isActive: boolean;
  sections: ProcedureSectionInput[];
}

export interface ProcedureSectionInput {
  code: string;
  title: string;
  sortOrder: number;
  layout?: string;
  /** CFD-09 — qué pinta la sección. Sin él, el upsert lo deja en `generic_form`. */
  sectionType?: WizardSectionType;
  formFields: FormFieldInput[];
}

export interface FormFieldInput {
  fieldKey: string;
  label: string;
  fieldType: string;
  isRequired: boolean;
  sortOrder: number;
  options?: string | null;
  validationSchema?: string | null;
  defaultValue?: string | null;
}

export interface ApplyTemplateFieldsRequest {
  procedureTypeId: string;
  sectionId: string;
}

// ── Configurador de tipos de trámite (ADR-0050) ──────────────────────────────

/**
 * Capacidades del tipo, tal como viven en `procedure_types.gate_profile`.
 *
 * Es el mismo objeto que gobierna los gates del backend y que el asistente recibe en su estado. El
 * configurador lo edita entero; las claves ausentes equivalen a «no exige».
 */
export interface GateProfile {
  /** `VIN` (vehículo aún sin placa), `PLATE` o `BOTH`. */
  entryMode?: string | null;
  requiresSeller?: boolean;
  requiresBuyer?: boolean;
  allowsMultipleBuyer?: boolean;
  allowsMultipleSeller?: boolean;
  requiresCommercialValue?: boolean;
  commercialValueSource?: string | null;
  requiresBiometrics?: boolean;
  /** Actores a validar: `OWNER`, `BUYER`. */
  biometricActors?: string[];
  requiresSignature?: boolean;
  requiresPlateRequest?: boolean;
  validateCompanyRule?: boolean;
  validateOtOperability?: boolean;
  validateDuplicateProcedure?: boolean;
  validateSoat?: boolean;
  validatePazSalvoImpuesto?: boolean;
  hasPrendaGate?: boolean;
  simitMode?: string | null;
}

/** Documento que el tipo exige (CFD-06). Se referencia por código del catálogo. */
export interface ConformationDocumentRequirement {
  documentTypeCode: string;
  isRequired: boolean;
  isDummy: boolean;
  conditionGroup?: string | null;
  sortOrder?: number;
}

/** Regla de conformación: qué actores intervienen y con qué perfil de validación. */
export interface ConformationRuleProfile {
  entityCode: string;
  validationProfile: Record<string, unknown>;
}

/** Fuente externa que el tipo consulta (RUNT, SIMIT…), con su orden y configuración. */
export interface ConformationSource {
  sourceCode: string;
  executionOrder: number;
  config: Record<string, unknown>;
}

/** Perfil de conformación completo del tipo — lo que el configurador lee y escribe. */
export interface ConformationProfile {
  procedureTypeId: string;
  code: string;
  publicationStatus: PublicationStatus;
  version: number;
  gateProfile: GateProfile;
  conformationRules: ConformationRuleProfile[];
  sources: ConformationSource[];
  documentRequirements: ConformationDocumentRequirement[];
}

/** Cuerpo del PUT del perfil. Cada lista ausente (`undefined`) significa «no tocar». */
export interface UpdateConformationProfileRequest {
  gateProfile?: GateProfile | null;
  sources?: ConformationSource[];
  conformationRules?: { entityCode: string; validationProfile?: Record<string, unknown>; isActive?: boolean; sortOrder?: number }[];
  documentRequirements?: ConformationDocumentRequirement[];
}

/** Cuerpo del PUT de identidad del tipo. `family` ausente la deja como está. */
export interface UpdateProcedureTypeRequest {
  name: string;
  description?: string | null;
  isActive: boolean;
  family?: ProcedureFamily;
}

/** Respuesta 422 al intentar habilitar un tipo que aún no está listo. */
export interface WizardEnabledNotReady {
  motivos: string[];
}

/**
 * Equivalencia del tipo con Quipux — el bloque `external_refs.quipux`.
 *
 * `tipoTramite`, `tipoRequisito` y `prefijo` los asigna la SECRETARÍA: no son derivables de nada que
 * FLIT sepa. El resto sí se puede proponer desde la parametrización del tipo.
 */
export interface MapeoQuipux {
  /** Taxonomía de la secretaría: MATRICULA | TRASPASO | OTROS. No tiene por qué coincidir con la de FLIT. */
  familia: string;
  tipoTramite: number;
  tipoRequisito: number;
  prefijo: string;
  /** Field value del que sale la placa, o null si el trámite no la usa. */
  campoPlaca: string | null;
  /** Field value del que sale el VIN, o null si el trámite no lo usa. */
  campoVin: string | null;
  maxLongitudEmpresa: number;
}


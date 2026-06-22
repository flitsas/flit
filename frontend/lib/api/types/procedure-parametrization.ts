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
  publishedAt: string | null;
}

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
  code: string;
  name: string;
  family: ProcedureFamily;
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

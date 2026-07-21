// FEATURE-08 (Configurador Dinámico) — tipos del perfil de conformación (frontend).
// Espeja el contrato backend §6.3 (GET/PUT /procedure-types/{id}/conformation-profile).

/** Modo de entrada del vehículo al trámite (CFD-02). */
export type EntryMode = 'PLATE' | 'VIN' | 'BOTH';

export const ENTRY_MODES: readonly EntryMode[] = ['VIN', 'PLATE', 'BOTH'] as const;

/**
 * Perfil de conformación (gate_profile) del tipo. Objeto abierto: HU-FE-01 maneja entryMode y las
 * banderas de validación inicial (CFD-02/03); las HUs siguientes añaden comercial/identidad/firma/placa.
 */
export interface GateProfile {
  entryMode?: EntryMode;
  validateCompanyRule?: boolean;
  validateOtOperability?: boolean;
  validateDuplicateProcedure?: boolean;
  requiresCommercialValue?: boolean;
  commercialValueSource?: string | null;
  requiresBiometrics?: boolean;
  biometricActors?: string[];
  requiresSignature?: boolean;
  requiresPlateRequest?: boolean;
  [key: string]: unknown;
}

/** Fuente externa a habilitar por el tipo (CFD-04). `config` lleva p. ej. `simitMode`. */
export interface ConformationSourceInput {
  sourceCode: string;
  executionOrder: number;
  config?: Record<string, unknown>;
}

/** Regla de conformación (actor) a persistir con su perfil de validación (CFD-05, incl. LESSEE). */
export interface ConformationRuleInput {
  entityCode: string;
  validationProfile?: Record<string, unknown>;
  isActive?: boolean;
  sortOrder?: number;
}

/**
 * Cuerpo del PUT /conformation-profile. HU-FE-01 envía gateProfile; HU-FE-02 añade sources +
 * conformationRules; HU-FE-03 añade documentRequirements. Campos opcionales: se envían solo los que
 * cambian.
 */
export interface ConformationProfileInput {
  gateProfile?: GateProfile;
  sources?: ConformationSourceInput[];
  conformationRules?: ConformationRuleInput[];
  documentRequirements?: {
    documentTypeCode: string;
    isRequired?: boolean;
    isDummy?: boolean;
    conditionGroup?: string | null;
    sortOrder?: number;
  }[];
}

export interface ConformationRuleProfile {
  entityCode: string;
  validationProfile: Record<string, unknown>;
}

export interface ProcedureSourceProfile {
  sourceCode: string;
  executionOrder: number;
  config: Record<string, unknown>;
}

export interface ProcedureDocumentRequirementProfile {
  documentTypeCode: string;
  isRequired: boolean;
  isDummy: boolean;
  conditionGroup: string | null;
}

/** Respuesta completa del GET /conformation-profile. */
export interface ProcedureConformationProfile {
  procedureTypeId: string;
  code: string;
  publicationStatus: string;
  version: number;
  gateProfile: GateProfile;
  conformationRules: ConformationRuleProfile[];
  sources: ProcedureSourceProfile[];
  documentRequirements: ProcedureDocumentRequirementProfile[];
}

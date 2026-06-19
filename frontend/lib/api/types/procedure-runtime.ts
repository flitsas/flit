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
  formFieldId: string;
  fieldKey: string;
  valueText?: string | null;
  valueJson?: string | null;
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

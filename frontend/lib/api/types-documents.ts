// Tipos del contrato API de gestión documental (HU #10198). Alineados manualmente
// con `contracts/openapi/core-api.v1.yaml` (sin codegen). Serialización camelCase.
// Cubren catálogo de documentos (#10193), asociaciones trámite↔doc (#10195),
// overrides de orden OT/Cliente (#10196) y matriz resuelta (RF18).

// ── Catálogo de tipos de documento (#10193, AC1) ────────────────────────────
export type DocumentTypeEstado = "activo" | "inactivo";

export interface DocumentType {
  id: string;
  codigo: string;
  nombre: string;
  descripcion?: string | null;
  estado: DocumentTypeEstado;
  fechaCreacion: string;
}

export interface DocumentTypePagedResult {
  data: DocumentType[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface DocumentTypeListParams {
  page?: number;
  pageSize?: number;
  includeInactive?: boolean;
}

/** Payload del POST /api/v1/admin/document-types (alta de catálogo). */
export interface CreateDocumentTypeRequest {
  codigo: string;
  nombre: string;
  descripcion?: string | null;
}

/** Payload del PUT /api/v1/admin/document-types/{id} (edición de catálogo). */
export interface UpdateDocumentTypeRequest {
  codigo: string;
  nombre: string;
  descripcion?: string | null;
}

// ── Asociaciones trámite ↔ documento (#10195, AC2) ──────────────────────────
export interface DocumentRef {
  codigo: string;
  nombre: string;
  estado: DocumentTypeEstado;
}

export interface ProcedureDocumentRequirement {
  id: string;
  procedureTypeId: string;
  documentTypeId: string;
  ordenDefault: number;
  obligatorio: boolean;
  documento: DocumentRef;
}

export interface CreateProcedureDocumentRequirementRequest {
  procedureTypeId: string;
  documentTypeId: string;
  ordenDefault: number;
  obligatorio: boolean;
}

export interface UpdateProcedureDocumentRequirementRequest {
  ordenDefault: number;
  obligatorio: boolean;
}

// ── Overrides de orden por OT / Cliente (#10196, AC3/AC4) ────────────────────
export type OverrideScope = "OT" | "CLIENTE";

export interface OverrideDocumentRef {
  codigo: string;
  nombre: string;
}

export interface DocumentOrderOverride {
  id: string;
  procedureTypeId: string;
  documentTypeId: string;
  scope: OverrideScope;
  /** transitOfficeId (scope OT) o clienteId (scope CLIENTE). */
  scopeRefId: string;
  orden: number;
  documento: OverrideDocumentRef;
}

/**
 * Payload del POST /api/v1/admin/document-order-overrides. El `scope` viaja como
 * query; según el scope se envía `transitOfficeId` (OT) o `clienteId` (CLIENTE).
 */
export interface CreateDocumentOrderOverrideRequest {
  procedureTypeId: string;
  documentTypeId: string;
  transitOfficeId?: string;
  clienteId?: string;
  orden: number;
}

// ── Matriz documental resuelta (RF18, AC5) ──────────────────────────────────
export type ResolutionLevel = "CLIENTE" | "OT" | "DEFAULT";

/** Etiquetas legibles del nivel que ganó la precedencia. */
export const RESOLUTION_LEVEL_LABELS: Record<ResolutionLevel, string> = {
  CLIENTE: "Cliente",
  OT: "OT",
  DEFAULT: "Default",
};

export interface ResolvedDocumentMatrixRow {
  documentTypeId: string;
  codigo: string;
  nombre: string;
  obligatorio: boolean;
  ordenResuelto: number;
  nivelAplicado: ResolutionLevel;
}

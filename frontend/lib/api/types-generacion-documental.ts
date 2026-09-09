/**
 * Tipos del módulo "Generación documental" (HU-01, Feature #12201).
 *
 * Contrato: `.claude/state/diseno-feature-12201.md` §7.1/§7.2 —
 * base `/api/v1/admin/generacion-documental`, permisos `generacion-documental.read` /
 * `.generate`. `POST .../generate` responde SIEMPRE `application/json` con `{ id, status }`
 * y NUNCA el binario: la descarga va por `GET /{id}/download` (presigned URL).
 *
 * `document_snapshot` (PII alta) no se modela aquí a propósito: no se expone en listados y
 * el detalle de HU-01 no lo consume.
 */
import type { StandaloneDocumentStatus } from "@/components/admin/generacion-documental/status-labels";

export type StandaloneDocumentType = "certificado_rues" | "transferencia_dominio_generada";

/** Escenario normativo A/B/C — solo en transferencia (I2); `null` en Certificado RUES. */
export type StandaloneDocumentScenario = "A" | "B" | "C";

/** Fila del historial (CF-17): metadata mínima, sin snapshot. */
export interface StandaloneDocumentListItem {
  id: string;
  documentType: StandaloneDocumentType;
  scenario: StandaloneDocumentScenario | null;
  status: StandaloneDocumentStatus;
  errorCode?: string | null;
  filename?: string | null;
  companyName?: string | null;
  createdByUserName?: string | null;
  createdAt: string;
}

export interface StandaloneDocumentsPagedResult {
  items: StandaloneDocumentListItem[];
  page: number;
  pageSize: number;
  total: number;
}

/**
 * Filtros del historial. `status` viaja como los estados INTERNOS que cubre la opción de
 * usuario (`standaloneDocumentStatusQuery`), porque «En proceso» son dos: pending y
 * processing (CF-21).
 */
export interface StandaloneDocumentsListParams {
  documentType?: StandaloneDocumentType;
  status?: StandaloneDocumentStatus[];
  dateFrom?: string;
  dateTo?: string;
  userId?: string;
  /** Solo SuperAdmin: metadata global de otro tenant (CF-20). Nunca devuelve contenido. */
  tenantId?: string;
  page?: number;
  pageSize?: number;
}

/** Respuesta de `POST /rues/generate` y `POST /transferencia/generate`. */
export interface StandaloneDocumentGenerateResult {
  id: string;
  status: StandaloneDocumentStatus;
}

/** Respuesta de `GET /{id}/download` (CF-19). La URL no se loguea nunca. */
export interface StandaloneDocumentDownloadLink {
  url: string;
  expiresAt: string;
}

/** Campo devuelto por la revisión previa de RUES (`POST /rues/preview`). */
export interface StandaloneRuesPreviewField {
  key: string;
  label: string;
  value: string | null;
}

export interface StandaloneRuesPreviewResult {
  found: boolean;
  nit: string;
  fields: StandaloneRuesPreviewField[];
}

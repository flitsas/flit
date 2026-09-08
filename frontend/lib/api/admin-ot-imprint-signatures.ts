// Cliente admin OT — improntas firmadas y validación de firma (HU #12148 / #12149 / #12176).
import { apiFetch } from "./client";

const base = "/api/v1/admin/ot";

export type ImprintSignatureValidationResultKind = "valid" | "invalid" | "not_found";

/** Fila de bitácora tramites.imprint_signature_validations. */
export interface ImprintSignatureValidationSummary {
  id: string;
  result: ImprintSignatureValidationResultKind;
  failureReason: string | null;
  validatedAt: string;
  validatedBy: string;
  placa: string;
}

/** Auditoría de impronta manual firmada. Deliberadamente NO incluye privateKey. */
export interface ImprintSignatureDto {
  id: string;
  tenantId: string;
  procedureInstanceId: string;
  placa: string;
  moduleCode: string;
  attachmentId: string | null;
  publicKey: string;
  documentHash: string;
  signature: string;
  signedAt: string;
  wasSignedWithoutOwnerSignature: boolean;
  signedStoragePath: string | null;
  signedSha256: string | null;
  signedSizeBytes: number | null;
  signedFilename: string | null;
  deletedAt: string | null;
  lastValidation?: ImprintSignatureValidationSummary | null;
}

export interface ImprintSignatureValidationResult {
  validationId?: string;
  vehicleSignatureImprintId: string;
  result: ImprintSignatureValidationResultKind;
  failureReason: string | null;
  validatedAt: string;
}

interface ImprintSignaturesListResponse {
  data: ImprintSignatureDto[];
}

interface ImprintSignatureValidationsListResponse {
  data: ImprintSignatureValidationSummary[];
}

export interface OtImprintSignatureScope {
  transitOfficeId?: string;
}

export function fetchListImprintSignatures(
  placa: string,
  signal?: AbortSignal,
  scope?: OtImprintSignatureScope,
): Promise<ImprintSignatureDto[]> {
  return apiFetch<ImprintSignaturesListResponse>(`${base}/imprint-signatures`, {
    query: {
      placa: placa.trim(),
      ...(scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : {}),
    },
    signal,
  }).then((response) => response.data);
}

/** Valida la firma digital pegada desde el PDF contra la impronta registrada. */
export function validateImprintSignature(
  id: string,
  signature: string,
  signal?: AbortSignal,
  scope?: OtImprintSignatureScope,
): Promise<ImprintSignatureValidationResult> {
  return apiFetch<ImprintSignatureValidationResult>(`${base}/imprint-signatures/${id}/validate`, {
    method: "POST",
    body: { signature },
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    signal,
  });
}

/** Historial append-only de validaciones de una impronta (HU #12176 / #12177). */
export function fetchImprintSignatureValidations(
  id: string,
  signal?: AbortSignal,
  scope?: OtImprintSignatureScope,
): Promise<ImprintSignatureValidationSummary[]> {
  return apiFetch<ImprintSignatureValidationsListResponse>(`${base}/imprint-signatures/${id}/validations`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    signal,
  }).then((response) => response.data);
}

/** Indica si la fila tiene PDF firmado (snapshot o adjunto vigente). */
export function imprintHasPdf(row: Pick<ImprintSignatureDto, "attachmentId" | "signedStoragePath">): boolean {
  return Boolean(row.signedStoragePath?.trim() || row.attachmentId);
}

/** URL presignada inline del PDF firmado de la impronta (HU #12173 / #12174). */
export function fetchImprintSignaturePreviewUrl(
  id: string,
  signal?: AbortSignal,
  scope?: OtImprintSignatureScope,
): Promise<{ url: string; expiresAt: string }> {
  return apiFetch<{ url: string; expiresAt: string }>(`${base}/imprint-signatures/${id}/preview-url`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    signal,
  });
}

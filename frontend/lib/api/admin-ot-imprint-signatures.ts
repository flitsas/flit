// Cliente admin OT — improntas firmadas y validación de firma (HU #12148 / #12149).
import { apiFetch } from "./client";

const base = "/api/v1/admin/ot";

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
}

export type ImprintSignatureValidationResultKind = "valid" | "invalid" | "not_found";

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

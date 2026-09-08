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

export function fetchListImprintSignatures(
  placa: string,
  signal?: AbortSignal,
): Promise<ImprintSignatureDto[]> {
  return apiFetch<ImprintSignaturesListResponse>(`${base}/imprint-signatures`, {
    query: { placa: placa.trim() },
    signal,
  }).then((response) => response.data);
}

export function validateImprintSignature(
  id: string,
  signal?: AbortSignal,
): Promise<ImprintSignatureValidationResult> {
  return apiFetch<ImprintSignatureValidationResult>(`${base}/imprint-signatures/${id}/validate`, {
    method: "POST",
    signal,
  });
}

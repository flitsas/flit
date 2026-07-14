// Cliente tipado de la API admin OT (HU #10215–#10220).
import { API_BASE_URL, apiFetch, getToken } from "./client";
import { downloadFile } from "./download";
import { ApiError } from "./types";
import type {
  CreateOtWebhookRequest,
  CreateOtDocumentTagRequest,
  CreateOtRuleRequest,
  OtApiLogsPagedResult,
  OtApiLogsParams,
  OtBandejaHealth,
  OtClientProcedure,
  OtClientProcedurePagedResult,
  OtClientProceduresParams,
  OtDocumentPrecedenceListResult,
  OtDocumentTag,
  OtDocumentTagsListResult,
  OtFeatureFlag,
  OtProfile,
  OtRequirements,
  OtRule,
  OtRulesListResult,
  OtWebhook,
  OtWebhooksListResult,
  RejectOtClientProcedureRequest,
  UpdateOtDocumentPrecedenceRequest,
  UpdateOtFeatureFlagRequest,
  UpdateOtProfileRequest,
  UpdateOtRequirementsRequest,
  UpdateOtRuleRequest,
  UpdateOtWebhookRequest,
} from "./types-ot";

const base = "/api/v1/admin/ot";

export interface OtApiScope {
  transitOfficeId?: string;
}

export function fetchOtProfile(
  signal?: AbortSignal,
  scope?: OtApiScope,
): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    signal,
  });
}

export function updateOtProfile(body: UpdateOtProfileRequest): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { method: "PATCH", body });
}

export function fetchOtRequirements(
  signal?: AbortSignal,
  scope?: OtApiScope,
): Promise<OtRequirements> {
  return apiFetch<OtRequirements>(`${base}/requirements`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    signal,
  });
}

export function updateOtRequirements(
  body: UpdateOtRequirementsRequest,
  scope?: OtApiScope,
): Promise<OtRequirements> {
  return apiFetch<OtRequirements>(`${base}/requirements`, {
    method: "PUT",
    body,
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
  });
}

export function updateOtFeatureFlag(
  id: string,
  body: UpdateOtFeatureFlagRequest,
): Promise<OtFeatureFlag> {
  return apiFetch<OtFeatureFlag>(`${base}/feature-flags/${id}`, { method: "PATCH", body });
}

export function fetchOtClientProcedures(
  params: OtClientProceduresParams = {},
  signal?: AbortSignal,
  scope?: OtApiScope,
): Promise<OtClientProcedurePagedResult> {
  return apiFetch<OtClientProcedurePagedResult>(`${base}/client-procedures`, {
    query: {
      ...params,
      ...(scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : {}),
    },
    signal,
  });
}

/** Diagnóstico de la bandeja OT (HU #10541 / R09): entregados con/sin grant vigente. */
export function fetchOtBandejaHealth(
  signal?: AbortSignal,
  scope?: OtApiScope,
): Promise<OtBandejaHealth> {
  return apiFetch<OtBandejaHealth>(`${base}/client-procedures/health`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    signal,
  });
}

export function approveOtClientProcedure(id: string): Promise<OtClientProcedure> {
  return apiFetch<OtClientProcedure>(`${base}/client-procedures/${id}/approve`, {
    method: "POST",
  });
}

export function rejectOtClientProcedure(
  id: string,
  body: RejectOtClientProcedureRequest,
): Promise<OtClientProcedure> {
  return apiFetch<OtClientProcedure>(`${base}/client-procedures/${id}/reject`, {
    method: "POST",
    body,
  });
}

/** Adjunto devuelto por los endpoints de expediente OT (shape del AttachmentDto de trámites). */
export interface OtProcedureAttachment {
  id: string;
  tipo: string;
  filename: string;
  mimetype: string;
  sizeBytes: number;
  sha256: string;
  source: string;
  uploadedAt: string;
}

/** Genera (o regenera) el expediente consolidado de un trámite de cliente OT. */
export function generarOtConsolidado(
  id: string,
  scope?: OtApiScope,
): Promise<{ document: { attachmentId: string; tipo: string; filename: string; sha256: string } }> {
  return apiFetch(`${base}/client-procedures/${id}/consolidado`, {
    method: "POST",
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
  });
}

/** Descarga el PDF del consolidado más reciente de un trámite de cliente OT. */
export function descargarOtConsolidado(
  id: string,
  referenceNumber?: string,
  scope?: OtApiScope,
): Promise<void> {
  return downloadFile(`${base}/client-procedures/${id}/consolidado`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    fallbackFilename: `consolidado_${referenceNumber ?? id}.pdf`,
  });
}

/**
 * Adjunta la Licencia de Tránsito (LT) al trámite de un cliente OT (multipart —
 * `apiFetch` es JSON-only, así que se usa fetch directo con FormData).
 */
export async function adjuntarOtLicenciaTransito(
  id: string,
  file: File,
  scope?: OtApiScope,
): Promise<OtProcedureAttachment> {
  const origin =
    API_BASE_URL || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(`${base}/client-procedures/${id}/attachments`, origin);
  if (scope?.transitOfficeId) {
    url.searchParams.set("transitOfficeId", scope.transitOfficeId);
  }

  const formData = new FormData();
  formData.append("file", file);

  const token = getToken();
  const response = await fetch(url.toString(), {
    method: "POST",
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: formData,
  });

  if (!response.ok) {
    let detail: unknown = null;
    try {
      detail = await response.json();
    } catch {
      /* error sin cuerpo JSON */
    }
    throw new ApiError(response.status, `Error ${response.status} al adjuntar la LT`, detail);
  }

  return (await response.json()) as OtProcedureAttachment;
}

/** Lista documentos del expediente OT (HU #10704/#10705). Respuesta BE: data + flags consolidado. */
export function fetchOtDocuments(
  id: string,
  scope?: OtApiScope,
): Promise<{
  data: OtProcedureAttachment[];
  consolidado: boolean;
  consolidado_maestro: boolean;
}> {
  return apiFetch<{
    data: OtProcedureAttachment[];
    consolidado: boolean;
    consolidado_maestro: boolean;
  }>(`${base}/client-procedures/${id}/documents`, {
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
  });
}

/** URL presignada de previsualización inline para un adjunto de trámite OT (ADR-0029). */
export function fetchOtAttachmentPreviewUrl(
  id: string,
  attachmentId: string,
  scope?: OtApiScope,
): Promise<{ url: string; expiresAt: string }> {
  return apiFetch<{ url: string; expiresAt: string }>(
    `${base}/client-procedures/${id}/documents/${attachmentId}/preview-url`,
    {
      query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
    },
  );
}

/**
 * Genera el expediente consolidado maestro de un trámite OT (incluye todos los
 * documentos del expediente). Endpoint disponible solo si el backend lo expone.
 */
export function generarOtConsolidadoMaestro(
  id: string,
  scope?: OtApiScope,
): Promise<{ document: { attachmentId: string; tipo: string; filename: string; sha256: string } }> {
  return apiFetch(`${base}/client-procedures/${id}/consolidado-maestro`, {
    method: "POST",
    query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
  });
}

export function fetchOtWebhooks(signal?: AbortSignal): Promise<OtWebhooksListResult> {
  return apiFetch<OtWebhooksListResult>(`${base}/webhooks`, { signal });
}

export function createOtWebhook(body: CreateOtWebhookRequest): Promise<OtWebhook> {
  return apiFetch<OtWebhook>(`${base}/webhooks`, { method: "POST", body });
}

export function updateOtWebhook(id: string, body: UpdateOtWebhookRequest): Promise<OtWebhook> {
  return apiFetch<OtWebhook>(`${base}/webhooks/${id}`, { method: "PATCH", body });
}

export function fetchOtApiLogs(
  params: OtApiLogsParams = {},
  signal?: AbortSignal,
): Promise<OtApiLogsPagedResult> {
  return apiFetch<OtApiLogsPagedResult>(`${base}/api-logs`, { query: { ...params }, signal });
}

export function fetchOtRules(signal?: AbortSignal): Promise<OtRulesListResult> {
  return apiFetch<OtRulesListResult>(`${base}/rules`, { signal });
}

export function createOtRule(body: CreateOtRuleRequest): Promise<OtRule> {
  return apiFetch<OtRule>(`${base}/rules`, { method: "POST", body });
}

export function updateOtRule(id: string, body: UpdateOtRuleRequest): Promise<OtRule> {
  return apiFetch<OtRule>(`${base}/rules/${id}`, { method: "PATCH", body });
}

export function fetchOtDocumentPrecedence(
  procedureTypeId: string,
  signal?: AbortSignal,
): Promise<OtDocumentPrecedenceListResult> {
  return apiFetch<OtDocumentPrecedenceListResult>(`${base}/document-precedence`, {
    query: { procedureTypeId },
    signal,
  });
}

export function updateOtDocumentPrecedence(
  body: UpdateOtDocumentPrecedenceRequest,
): Promise<OtDocumentPrecedenceListResult> {
  return apiFetch<OtDocumentPrecedenceListResult>(`${base}/document-precedence`, {
    method: "PATCH",
    body,
  });
}

export function fetchOtDocumentTags(signal?: AbortSignal): Promise<OtDocumentTagsListResult> {
  return apiFetch<OtDocumentTagsListResult>(`${base}/document-tags`, { signal });
}

export function createOtDocumentTag(body: CreateOtDocumentTagRequest): Promise<OtDocumentTag> {
  return apiFetch<OtDocumentTag>(`${base}/document-tags`, { method: "POST", body });
}

export function deleteOtDocumentTag(id: string): Promise<void> {
  return apiFetch<void>(`${base}/document-tags/${id}`, { method: "DELETE" });
}

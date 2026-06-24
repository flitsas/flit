// Cliente tipado de la API admin OT (HU #10215–#10220).
import { apiFetch } from "./client";
import type {
  CreateOtWebhookRequest,
  CreateOtDocumentTagRequest,
  CreateOtRuleRequest,
  OtApiLogsPagedResult,
  OtApiLogsParams,
  OtClientProcedure,
  OtClientProcedurePagedResult,
  OtClientProceduresParams,
  OtDocumentPrecedenceListResult,
  OtDocumentTag,
  OtDocumentTagsListResult,
  OtFeatureFlag,
  OtProfile,
  OtRule,
  OtRulesListResult,
  OtWebhook,
  OtWebhooksListResult,
  RejectOtClientProcedureRequest,
  UpdateOtDocumentPrecedenceRequest,
  UpdateOtFeatureFlagRequest,
  UpdateOtProfileRequest,
  UpdateOtRuleRequest,
  UpdateOtWebhookRequest,
} from "./types-ot";

const base = "/api/v1/admin/ot";

export function fetchOtProfile(signal?: AbortSignal): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { signal });
}

export function updateOtProfile(body: UpdateOtProfileRequest): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { method: "PATCH", body });
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
): Promise<OtClientProcedurePagedResult> {
  return apiFetch<OtClientProcedurePagedResult>(`${base}/client-procedures`, {
    query: { ...params },
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

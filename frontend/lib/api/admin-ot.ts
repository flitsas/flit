// Cliente tipado de la API admin OT (HU #10215–#10220).
import { apiFetch } from "./client";
import type {
  CreateOtWebhookRequest,
  OtApiLogsPagedResult,
  OtApiLogsParams,
  OtClientProcedure,
  OtClientProcedurePagedResult,
  OtClientProceduresParams,
  OtProfile,
  OtWebhook,
  OtWebhooksListResult,
  RejectOtClientProcedureRequest,
  UpdateOtProfileRequest,
  UpdateOtWebhookRequest,
} from "./types-ot";

const base = "/api/v1/admin/ot";

export function fetchOtProfile(signal?: AbortSignal): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { signal });
}

export function updateOtProfile(body: UpdateOtProfileRequest): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { method: "PATCH", body });
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

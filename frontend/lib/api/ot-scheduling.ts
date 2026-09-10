// Cliente tipado de "Programación y alertas" con alcance Organismo de Tránsito (Reportes 2.0,
// HU-D, tercera ola). Mismos tipos que analytics-scheduling.ts (vocabulario compartido); solo
// cambian las rutas (/api/v1/admin/ot/*) y el parámetro de scope (transitOfficeId en vez de
// tenantId) — ver AdminOtReportSchedulesEndpoints/AdminOtAlertRulesEndpoints en el backend.
import { apiFetch } from "./client";
import type {
  AlertEvent,
  AlertEventsPage,
  AlertRule,
  AlertRuleInput,
  ReportSchedule,
  ReportScheduleInput,
} from "./analytics-scheduling";

const scheduleBase = "/api/v1/admin/ot/report-schedules";
const alertBase = "/api/v1/admin/ot/alert-rules";
const eventBase = "/api/v1/admin/ot/alert-events";

// ── Informes programados del organismo ───────────────────────────────────────

export function fetchOtReportSchedules(
  transitOfficeId?: string,
  signal?: AbortSignal,
): Promise<{ items: ReportSchedule[] }> {
  return apiFetch<{ items: ReportSchedule[] }>(scheduleBase, {
    query: { transitOfficeId },
    signal,
  });
}

export function createOtReportSchedule(
  input: ReportScheduleInput,
  transitOfficeId?: string,
): Promise<ReportSchedule> {
  return apiFetch<ReportSchedule>(scheduleBase, {
    method: "POST",
    body: input,
    query: { transitOfficeId },
  });
}

export function updateOtReportSchedule(
  id: string,
  input: ReportScheduleInput,
  transitOfficeId?: string,
): Promise<ReportSchedule> {
  return apiFetch<ReportSchedule>(`${scheduleBase}/${id}`, {
    method: "PUT",
    body: input,
    query: { transitOfficeId },
  });
}

export function deleteOtReportSchedule(id: string, transitOfficeId?: string): Promise<void> {
  return apiFetch<void>(`${scheduleBase}/${id}`, {
    method: "DELETE",
    query: { transitOfficeId },
  });
}

// ── Reglas de alerta del organismo ───────────────────────────────────────────

export function fetchOtAlertRules(
  transitOfficeId?: string,
  signal?: AbortSignal,
): Promise<{ items: AlertRule[] }> {
  return apiFetch<{ items: AlertRule[] }>(alertBase, {
    query: { transitOfficeId },
    signal,
  });
}

export function createOtAlertRule(input: AlertRuleInput, transitOfficeId?: string): Promise<AlertRule> {
  return apiFetch<AlertRule>(alertBase, {
    method: "POST",
    body: input,
    query: { transitOfficeId },
  });
}

export function updateOtAlertRule(
  id: string,
  input: AlertRuleInput,
  transitOfficeId?: string,
): Promise<AlertRule> {
  return apiFetch<AlertRule>(`${alertBase}/${id}`, {
    method: "PUT",
    body: input,
    query: { transitOfficeId },
  });
}

export function deleteOtAlertRule(id: string, transitOfficeId?: string): Promise<void> {
  return apiFetch<void>(`${alertBase}/${id}`, {
    method: "DELETE",
    query: { transitOfficeId },
  });
}

// ── Historial de disparos del organismo ──────────────────────────────────────

export interface OtAlertEventsParams {
  ruleId?: string;
  page?: number;
  pageSize?: number;
  transitOfficeId?: string;
}

export function fetchOtAlertEvents(
  params: OtAlertEventsParams = {},
  signal?: AbortSignal,
): Promise<AlertEventsPage> {
  return apiFetch<AlertEventsPage>(eventBase, {
    query: {
      ruleId: params.ruleId,
      page: params.page,
      pageSize: params.pageSize,
      transitOfficeId: params.transitOfficeId,
    },
    signal,
  });
}

export function acknowledgeOtAlertEvent(
  id: string,
  transitOfficeId?: string,
  signal?: AbortSignal,
): Promise<AlertEvent> {
  return apiFetch<AlertEvent>(`${eventBase}/${id}/ack`, {
    method: "POST",
    query: { transitOfficeId },
    signal,
  });
}

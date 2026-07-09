// Cliente tipado de programación de informes y alertas — Reportes 2.0, HU-D (§4.7 del
// contrato docs/contratos-reportes-v2.md). Tipos espejo camelCase de los DTOs del backend.
// Los tipos NUEVOS viven aquí (regla §2: lib/api/types.ts no se toca).
import { apiFetch } from "./client";

const base = "/api/v1/analytics";

// ── Tipos §4.7 ───────────────────────────────────────────────────────────────

export type ReportType = "resumen" | "operacion" | "ot" | "uso" | "productividad";
export type ScheduleFrequency = "daily" | "weekly" | "monthly";
export type ScheduleFormat = "excel" | "pdf";
export type AlertMetric =
  | "rejection_rate_pct"
  | "stuck_count"
  | "external_api_errors"
  | "pending_identity_validations";
export type AlertOperator = "gt" | "gte" | "lt" | "lte";

export interface ReportSchedule {
  id: string;
  name: string;
  reportType: ReportType;
  frequency: ScheduleFrequency;
  /** 0 (domingo) … 6 (sábado); solo weekly. */
  dayOfWeek: number | null;
  /** 1..28; solo monthly. */
  dayOfMonth: number | null;
  /** 0..23, hora de Bogotá. */
  sendHour: number;
  format: ScheduleFormat;
  recipients: string[];
  isActive: boolean;
  lastSentAt: string | null;
}

export interface AlertRule {
  id: string;
  name: string;
  metric: AlertMetric;
  operator: AlertOperator;
  threshold: number;
  windowMinutes: number;
  cooldownMinutes: number;
  recipients: string[];
  isActive: boolean;
  lastTriggeredAt: string | null;
}

export interface AlertEvent {
  id: string;
  alertRuleId: string;
  ruleName: string;
  triggeredAt: string;
  metricValue: number;
  threshold: number;
  notified: boolean;
  message: string | null;
}

export interface AlertEventsPage {
  items: AlertEvent[];
  totalCount: number;
}

/** Payload de creación/edición de un informe programado (POST/PUT). */
export interface ReportScheduleInput {
  name: string;
  reportType: ReportType;
  frequency: ScheduleFrequency;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  sendHour: number;
  format: ScheduleFormat;
  recipients: string[];
  isActive: boolean;
}

/** Payload de creación/edición de una regla de alerta (POST/PUT). */
export interface AlertRuleInput {
  name: string;
  metric: AlertMetric;
  operator: AlertOperator;
  threshold: number;
  windowMinutes: number;
  cooldownMinutes: number;
  recipients: string[];
  isActive: boolean;
}

export interface AlertEventsParams {
  ruleId?: string;
  page?: number;
  pageSize?: number;
  /** Solo SuperAdmin (obligatorio para él: sin vista global en estos endpoints). */
  tenantId?: string;
}

// ── Informes programados ─────────────────────────────────────────────────────

export function fetchReportSchedules(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<{ items: ReportSchedule[] }> {
  return apiFetch<{ items: ReportSchedule[] }>(`${base}/report-schedules`, {
    query: { tenantId },
    signal,
  });
}

export function createReportSchedule(
  input: ReportScheduleInput,
  tenantId?: string,
): Promise<ReportSchedule> {
  return apiFetch<ReportSchedule>(`${base}/report-schedules`, {
    method: "POST",
    body: input,
    query: { tenantId },
  });
}

export function updateReportSchedule(
  id: string,
  input: ReportScheduleInput,
  tenantId?: string,
): Promise<ReportSchedule> {
  return apiFetch<ReportSchedule>(`${base}/report-schedules/${id}`, {
    method: "PUT",
    body: input,
    query: { tenantId },
  });
}

export function deleteReportSchedule(id: string, tenantId?: string): Promise<void> {
  return apiFetch<void>(`${base}/report-schedules/${id}`, {
    method: "DELETE",
    query: { tenantId },
  });
}

// ── Reglas de alerta ─────────────────────────────────────────────────────────

export function fetchAlertRules(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<{ items: AlertRule[] }> {
  return apiFetch<{ items: AlertRule[] }>(`${base}/alert-rules`, {
    query: { tenantId },
    signal,
  });
}

export function createAlertRule(input: AlertRuleInput, tenantId?: string): Promise<AlertRule> {
  return apiFetch<AlertRule>(`${base}/alert-rules`, {
    method: "POST",
    body: input,
    query: { tenantId },
  });
}

export function updateAlertRule(
  id: string,
  input: AlertRuleInput,
  tenantId?: string,
): Promise<AlertRule> {
  return apiFetch<AlertRule>(`${base}/alert-rules/${id}`, {
    method: "PUT",
    body: input,
    query: { tenantId },
  });
}

export function deleteAlertRule(id: string, tenantId?: string): Promise<void> {
  return apiFetch<void>(`${base}/alert-rules/${id}`, {
    method: "DELETE",
    query: { tenantId },
  });
}

// ── Historial de disparos ────────────────────────────────────────────────────

export function fetchAlertEvents(
  params: AlertEventsParams = {},
  signal?: AbortSignal,
): Promise<AlertEventsPage> {
  return apiFetch<AlertEventsPage>(`${base}/alert-events`, {
    query: {
      ruleId: params.ruleId,
      page: params.page,
      pageSize: params.pageSize,
      tenantId: params.tenantId,
    },
    signal,
  });
}

// Cliente tipado de programación de informes y alertas — Reportes 2.0, HU-D (§4.7 del
// contrato docs/contratos-reportes-v2.md). Tipos espejo camelCase de los DTOs del backend.
// Los tipos NUEVOS viven aquí (regla §2: lib/api/types.ts no se toca).
import { apiFetch } from "./client";

const base = "/api/v1/analytics";

// ── Tipos §4.7 ───────────────────────────────────────────────────────────────

export type ReportType =
  | "resumen"
  | "operacion"
  | "ot"
  | "uso"
  | "productividad"
  | "consulta"
  // Alcance Organismo de Tránsito (Reportes 2.0, HU-D, tercera ola): uno por pestaña con rango de
  // OtReportsConsole.tsx. "Ahora mismo" queda fuera (snapshot en vivo, sin rango).
  | "ot_analisis"
  | "ot_informe"
  | "ot_revisores"
  // Alcance ICT (Reportes 2.0, HU-D, cuarta ola): detalle fila a fila del pipeline de
  // Integración con Terceros, sin versión PDF con sentido (mismo criterio que "consulta"). Solo
  // se entregan en Excel — validado también en el backend (SchedulingValidation). "ict_jobs" es
  // el único de los cuatro restringido a SuperAdmin: ict.job_runs es platform-wide, sin tenant_id.
  | "ict_novedades"
  | "ict_atascados"
  | "ict_jobs"
  | "ict_webhooks";
/** Solo aplica cuando reportType="consulta". "ot" = consulta guardada del organismo de tránsito
 * (Reportes 2.0, HU-D, tercera ola). */
export type SavedQueryScope = "empresa" | "superadmin" | "ot";
export type ScheduleFrequency = "daily" | "weekly" | "monthly";
export type ScheduleFormat = "excel" | "pdf";
export type AlertMetric =
  | "rejection_rate_pct"
  | "stuck_count"
  | "external_api_errors"
  | "pending_identity_validations"
  // Métricas ICT (HU5 / E1), evaluadas cross-schema sobre ict.*
  | "ict_stuck_in_validation"
  | "ict_novelty_rate_pct"
  | "ict_webhook_delivery_failures"
  | "ict_jobs_out_of_sla"
  // Métricas de alcance OT (Reportes 2.0, HU-D, tercera ola)
  | "ot_rejection_rate_pct"
  | "ot_stuck_count";
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
  /** Solo reportType="consulta". */
  savedQueryId?: string | null;
  /** Solo reportType="consulta". */
  savedQueryScope?: SavedQueryScope | null;
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
  /** Momento del reconocimiento (null = sin reconocer). */
  acknowledgedAt: string | null;
  /** Usuario que reconoció el disparo. */
  acknowledgedBy: string | null;
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
  /** Solo reportType="consulta". */
  savedQueryId?: string | null;
  /** Solo reportType="consulta". */
  savedQueryScope?: SavedQueryScope | null;
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

// ── Informes de SuperAdmin (alcance "superadmin", todas las compañías) ──────
// Mismo modelo, endpoint separado y SIN tenant — ver SuperAdminReportSchedulesEndpoints
// en el backend. Solo crea/edita reportType="consulta" con savedQueryScope="superadmin".

export function fetchSuperAdminReportSchedules(signal?: AbortSignal): Promise<{ items: ReportSchedule[] }> {
  return apiFetch<{ items: ReportSchedule[] }>(`${base}/report-schedules/superadmin`, { signal });
}

export function createSuperAdminReportSchedule(input: ReportScheduleInput): Promise<ReportSchedule> {
  return apiFetch<ReportSchedule>(`${base}/report-schedules/superadmin`, {
    method: "POST",
    body: input,
  });
}

export function updateSuperAdminReportSchedule(
  id: string,
  input: ReportScheduleInput,
): Promise<ReportSchedule> {
  return apiFetch<ReportSchedule>(`${base}/report-schedules/superadmin/${id}`, {
    method: "PUT",
    body: input,
  });
}

export function deleteSuperAdminReportSchedule(id: string): Promise<void> {
  return apiFetch<void>(`${base}/report-schedules/superadmin/${id}`, { method: "DELETE" });
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

/** Reconoce (set-once) un disparo de alerta; devuelve el evento actualizado. */
export function acknowledgeAlertEvent(
  id: string,
  tenantId?: string,
  signal?: AbortSignal,
): Promise<AlertEvent> {
  return apiFetch<AlertEvent>(`${base}/alert-events/${id}/ack`, {
    method: "POST",
    query: { tenantId },
    signal,
  });
}

// Cliente tipado de los endpoints de métricas de Reportes 2.0 (HU-C).
// Tipos espejo camelCase EXACTOS del contrato `docs/contratos-reportes-v2.md` §4.
// Los endpoints viven bajo /api/v1/analytics y comparten la resolución de tenant
// de los existentes; para SuperAdmin los cuatro EXIGEN `tenantId` (400 si falta).
import { apiFetch } from "./client";

const base = "/api/v1/analytics";

// ── Comparación de periodos (§4.1) ──────────────────────────────────────────

/** Modo de comparación soportado por los endpoints de métricas. */
export type CompareMode = "previous_period" | "previous_year";

/** Ventana efectiva del periodo comparado, resuelta por el backend. */
export interface ComparisonInfo {
  mode: CompareMode;
  from: string;
  to: string;
}

/** Envoltura de una respuesta comparada: periodo actual + periodo anterior (o null). */
export interface Compared<T> {
  current: T;
  previous: T | null;
  comparison: ComparisonInfo | null;
}

/** Filtros comunes de los endpoints de métricas (§4.1). `from`/`to` en YYYY-MM-DD. */
export interface MetricsParams {
  from: string;
  to: string;
  /** Solo SuperAdmin; obligatorio para él en estos endpoints. */
  tenantId?: string;
  transitOfficeId?: string;
  procedureTypeId?: string;
  operatorUserId?: string;
  /** Estado N03: borrador | anulado | preparado | entregado | aprobado | rechazado. */
  status?: string;
  /** Substring de causal de rechazo (case-insensitive). */
  reason?: string;
  compareWith?: CompareMode;
  /** Umbral de días para "atascado" (default 7, rango 1..90). Solo ot-metrics/live-overview. */
  stuckDays?: number;
}

/** Parámetros del panel en vivo (§4.5): sin rango ni compareWith. */
export interface LiveOverviewParams {
  tenantId?: string;
  stuckDays?: number;
}

// ── GET /analytics/ot-metrics (§4.2) ─────────────────────────────────────────

export interface OtMetricsSummary {
  entregados: number;
  aprobados: number;
  rechazados: number;
  /** rechazados / (aprobados + rechazados) * 100. */
  rejectionRatePct: number;
  avgApprovalHours: number;
  p50ApprovalHours: number;
  p90ApprovalHours: number;
  /** % de rechazados que volvieron a borrador. */
  reincidencePct: number;
  stuckCount: number;
}

export interface RejectionByOffice {
  transitOfficeId: string;
  transitOfficeName: string;
  entregados: number;
  aprobados: number;
  rechazados: number;
  rejectionRatePct: number;
}

export interface RejectionByReason {
  reason: string;
  count: number;
  pct: number;
}

export interface RejectionByType {
  procedureTypeId: string;
  procedureTypeName: string;
  entregados: number;
  rechazados: number;
  rejectionRatePct: number;
}

export interface ApprovalTimesByOffice {
  transitOfficeId: string;
  transitOfficeName: string;
  decididos: number;
  avgHours: number;
  p50Hours: number;
  p90Hours: number;
}

export interface OfficeRankingItem {
  transitOfficeId: string;
  transitOfficeName: string;
  rank: number;
  p50Hours: number;
  rejectionRatePct: number;
  volumen: number;
}

export interface ReincidenceMetrics {
  rechazadas: number;
  reintentadas: number;
  avgCiclos: number;
  maxCiclos: number;
}

export interface StuckItem {
  instanceId: string;
  referenceNumber: string;
  status: string;
  daysInStatus: number;
  transitOfficeName: string;
  procedureTypeName: string;
  createdByDisplayName: string;
}

export interface StuckMetrics {
  totalCount: number;
  /** Top 50 por días en el estado. */
  items: StuckItem[];
}

/**
 * Causal TIPIFICADA del catálogo global.
 *
 * `pct` es el porcentaje de RECHAZOS que incluyen la causal, no el reparto de un total: un rechazo
 * puede llevar varias causales, así que la suma puede pasar del 100 %. Hay que rotularlo así al
 * pintarlo, porque leído como reparto el número engaña.
 */
export interface RejectionByReasonCatalog {
  reasonId: string;
  code: string;
  description: string;
  rechazos: number;
  pct: number;
}

/** Tramo PROPIO del ciclo: horas desde que se crea el trámite hasta que se entrega. */
export interface InternalCycle {
  avgHours: number | null;
  p50Hours: number | null;
  p90Hours: number | null;
}

export interface OtMetricsData {
  summary: OtMetricsSummary;
  rejectionByOffice: RejectionByOffice[];
  /** Motivos escritos a mano. Se conserva por los rechazos anteriores al catálogo; no es agregable. */
  rejectionByReason: RejectionByReason[];
  rejectionByType: RejectionByType[];
  approvalTimesByOffice: ApprovalTimesByOffice[];
  officeRanking: OfficeRankingItem[];
  reincidence: ReincidenceMetrics;
  stuck: StuckMetrics;
  /** Motivos del catálogo. Vacío mientras no haya rechazos tipificados en el rango. */
  rejectionByReasonCatalog: RejectionByReasonCatalog[];
  /**
   * Causales marcadas por rechazo (promedio). Indicador de salud: si se acerca al tamaño del
   * catálogo, alguien está marcando todo y la distribución deja de discriminar.
   */
  avgReasonsPerRejection: number;
  internalCycle: InternalCycle;
}

export type OtMetricsResponse = Compared<OtMetricsData>;

// ── GET /analytics/funnel (§4.3) ─────────────────────────────────────────────

export interface FunnelStage {
  stage: string;
  count: number;
  pctOfFirst: number;
  pctOfPrev: number;
}

/** Métrica agregada de un paso del wizard (telemetría HU-A, verbatim §6). */
export interface WizardStepMetric {
  stepKey: string;
  views: number;
  completions: number;
  abandonmentPct: number;
  avgDurationMs: number | null;
  medianDurationMs: number | null;
}

export interface FunnelData {
  states: FunnelStage[];
  anulados: number;
  /** Rechazados cuyo estado ACTUAL sigue siendo rechazado. */
  rechazadosVigentes: number;
  /** [] si aún no hay telemetría. */
  wizardSteps: WizardStepMetric[];
}

export type FunnelResponse = Compared<FunnelData>;

// ── GET /analytics/usage (§4.4) ──────────────────────────────────────────────

export interface ModuleUsage {
  module: string;
  events: number;
  uniqueUsers: number;
}

/** Celda del heatmap de horas pico: 0=domingo … 6=sábado, hora America/Bogota. */
export interface PeakHour {
  dayOfWeek: number;
  hour: number;
  events: number;
}

export interface DocumentReplacement {
  documentTipo: string;
  uploads: number;
  replacements: number;
}

export interface ExternalApiMetric {
  endpoint: string;
  direction: string;
  calls: number;
  errors: number;
  errorRatePct: number;
  avgDurationMs: number;
  p90DurationMs: number;
}

export interface UsageData {
  moduleUsage: ModuleUsage[];
  wizardSteps: WizardStepMetric[];
  peakHours: PeakHour[];
  documentReplacements: DocumentReplacement[];
  externalApis: ExternalApiMetric[];
  /** Wizard completo; null si no hay datos. */
  avgWizardDurationMs: number | null;
  medianWizardDurationMs: number | null;
}

export type UsageResponse = Compared<UsageData>;

// ── GET /analytics/live-overview (§4.5) ──────────────────────────────────────

export interface LiveStatusCount {
  status: string;
  count: number;
}

export interface LiveToday {
  creados: number;
  /** Estado ACTUAL de instancias activas (no finales) del tenant. */
  byStatus: LiveStatusCount[];
  entregados: number;
  aprobados: number;
  rechazados: number;
}

export interface IntegrationsLastHour {
  calls: number;
  errors: number;
  avgDurationMs: number;
}

export interface LiveOverviewResponse {
  generatedAt: string;
  today: LiveToday;
  stuckCount: number;
  pendingIdentityValidations: number;
  integrationsLastHour: IntegrationsLastHour;
  /** Último cambio de estado o evento; null si no hay actividad. */
  lastActivityAt: string | null;
}

// ── Fetchers ─────────────────────────────────────────────────────────────────

function metricsQuery(params: MetricsParams) {
  return {
    from: params.from,
    to: params.to,
    tenantId: params.tenantId,
    transitOfficeId: params.transitOfficeId,
    procedureTypeId: params.procedureTypeId,
    operatorUserId: params.operatorUserId,
    status: params.status,
    reason: params.reason,
    compareWith: params.compareWith,
    stuckDays: params.stuckDays,
  };
}

/** GET /ot-metrics — rechazos, tiempos de aprobación, ranking, reincidencia y atascados. */
export function fetchOtMetrics(params: MetricsParams, signal?: AbortSignal): Promise<OtMetricsResponse> {
  return apiFetch<OtMetricsResponse>(`${base}/ot-metrics`, { query: metricsQuery(params), signal });
}

/** GET /funnel — embudo de estados N03 + pasos del wizard (si hay telemetría). */
export function fetchFunnel(params: MetricsParams, signal?: AbortSignal): Promise<FunnelResponse> {
  return apiFetch<FunnelResponse>(`${base}/funnel`, { query: metricsQuery(params), signal });
}

/** GET /usage — uso del aplicativo: módulos, wizard, horas pico, documentos y APIs externas. */
export function fetchUsageMetrics(params: MetricsParams, signal?: AbortSignal): Promise<UsageResponse> {
  return apiFetch<UsageResponse>(`${base}/usage`, { query: metricsQuery(params), signal });
}

/** GET /live-overview — panel "Ahora mismo" (< 300 ms, sin comparación). */
export function fetchLiveOverview(params: LiveOverviewParams, signal?: AbortSignal): Promise<LiveOverviewResponse> {
  return apiFetch<LiveOverviewResponse>(`${base}/live-overview`, {
    query: { tenantId: params.tenantId, stuckDays: params.stuckDays },
    signal,
  });
}

// ── Helper único de variación (§5) ──────────────────────────────────────────

/**
 * Variación porcentual del periodo actual frente al comparado, redondeada a 1 decimal.
 * Devuelve `null` si no hay base de comparación (`previous` null/undefined o 0):
 * el backend NO manda deltas; TODA la UI calcula la variación con este helper.
 */
export function variationPct(current: number, previous: number | null | undefined): number | null {
  if (previous === null || previous === undefined || previous === 0) return null;
  return Math.round(((current - previous) / previous) * 1000) / 10;
}

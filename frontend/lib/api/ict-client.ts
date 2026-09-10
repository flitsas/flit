// Cliente tipado del submódulo de observabilidad ICT (HU10893, Feature #10888).
// Consume los endpoints de solo lectura de core-ict a través del Gateway:
//   GET /api/v1/ict/logs   (logs redactados/enmascarados)
//   GET /api/v1/ict/alerts (métricas de alerta ICT)
import { apiFetch } from "./client";

export type IctLogType = "auth" | "transaction" | "webhook" | "external";

/** Una entrada del log de integración (request/response/headers ya REDACTADOS/ENMASCARADOS). */
export interface IctLogEntry {
  id: string;
  tenantId: string | null;
  logType: IctLogType;
  direction: string;
  method: string;
  path: string;
  statusCode: number;
  request: string | null;
  response: string | null;
  headers: string | null;
  correlationId: string | null;
  durationMs: number;
  usuario: string | null;
  createdAt: string;
}

export interface IctLogPage {
  items: IctLogEntry[];
  total: number;
  page: number;
  pageSize: number;
}

export interface IctLogFilters {
  logType?: IctLogType;
  correlationId?: string;
  /** Búsqueda de texto sobre la ruta (p.ej. el número de trámite: "82"). */
  search?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export function fetchIctLogs(filters: IctLogFilters = {}, signal?: AbortSignal): Promise<IctLogPage> {
  return apiFetch<IctLogPage>("/api/v1/ict/logs", {
    query: {
      logType: filters.logType,
      correlationId: filters.correlationId,
      search: filters.search,
      from: filters.from,
      to: filters.to,
      page: filters.page,
      pageSize: filters.pageSize,
    },
    signal,
  });
}

/** Métricas de alerta ICT (trámites atascados, tasa de novedades, fallos de webhook, jobs fuera de SLA). */
export interface IctAlertMetrics {
  stuckInValidation: number;
  noveltyRatePct: number;
  webhookDeliveryFailures: number;
  jobsOutOfSla: number;
}

export function fetchIctAlerts(signal?: AbortSignal): Promise<IctAlertMetrics> {
  return apiFetch<IctAlertMetrics>("/api/v1/ict/alerts", { signal });
}

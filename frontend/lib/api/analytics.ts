// Cliente tipado del módulo Analytics · Dashboard (HU #10243 backend / #10247 frontend).
// Una función por endpoint del contrato `contracts/openapi/core-api.v1.yaml`.
import { apiFetch } from "./client";
import { downloadFile } from "./download";
import type {
  AnalyticsOverviewParams,
  AnalyticsOverviewResponse,
  ExecutivePdfParams,
  ProcedureDetailsPage,
  ProcedureDetailsParams,
  TopProducersParams,
  TopProducersResponse,
} from "./types";

const base = "/api/v1/analytics";

/**
 * GET /overview — métricas por categoría/estado del tenant en el rango [from, to]
 * (RF01, RF02). El tenant se toma del claim JWT `tenant_id`; un SuperAdmin puede
 * indicar `tenantId` para consultar otra compañía (AC1). Lanza `ApiError` con el
 * status del backend (400 rango inválido, 403 tenant ajeno, 401 sin sesión).
 */
export function fetchAnalyticsOverview(
  params: AnalyticsOverviewParams,
  signal?: AbortSignal,
): Promise<AnalyticsOverviewResponse> {
  return apiFetch<AnalyticsOverviewResponse>(`${base}/overview`, {
    query: { from: params.from, to: params.to, tenantId: params.tenantId },
    signal,
  });
}

/**
 * GET /productivity/top — ranking de radicadores por trámites enviados (RF07, HU #10248
 * AC2). Mismo modelo de tenant que el overview.
 */
export function fetchTopProducers(
  params: TopProducersParams,
  signal?: AbortSignal,
): Promise<TopProducersResponse> {
  return apiFetch<TopProducersResponse>(`${base}/productivity/top`, {
    query: { from: params.from, to: params.to, limit: params.limit, tenantId: params.tenantId },
    signal,
  });
}

/**
 * GET /procedures — detalle paginado de trámites filtrable por categoría y estado
 * (RF04, RF05, HU #10248 AC1). Devuelve la página y el `totalCount` del universo filtrado.
 */
export function fetchProcedureDetails(
  params: ProcedureDetailsParams,
  signal?: AbortSignal,
): Promise<ProcedureDetailsPage> {
  return apiFetch<ProcedureDetailsPage>(`${base}/procedures`, {
    query: {
      from: params.from,
      to: params.to,
      category: params.category,
      status: params.status,
      page: params.page,
      pageSize: params.pageSize,
      tenantId: params.tenantId,
    },
    signal,
  });
}

/**
 * GET /export/excel — descarga el .xlsx del detalle filtrado (RF06, HU #10249 AC1).
 * Usa los mismos filtros que `/procedures`. Lanza `ApiError` ante 4xx/5xx (AC3).
 */
export function exportAnalyticsExcel(params: ProcedureDetailsParams, signal?: AbortSignal): Promise<void> {
  return downloadFile(`${base}/export/excel`, {
    method: "GET",
    query: {
      from: params.from,
      to: params.to,
      category: params.category,
      status: params.status,
      tenantId: params.tenantId,
    },
    fallbackFilename: `tramites_${params.from}_${params.to}.xlsx`,
    signal,
  });
}

/**
 * POST /export/executive-pdf — descarga el PDF de Resumen Ejecutivo del periodo
 * (RF10, HU #10249 AC2). Lanza `ApiError` ante 4xx/5xx (AC3).
 */
export function exportExecutivePdf(params: ExecutivePdfParams, signal?: AbortSignal): Promise<void> {
  return downloadFile(`${base}/export/executive-pdf`, {
    method: "POST",
    body: { from: params.from, to: params.to, tenantId: params.tenantId },
    fallbackFilename: `resumen_ejecutivo_${params.from}_${params.to}.pdf`,
    signal,
  });
}

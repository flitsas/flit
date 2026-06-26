// Cliente tipado del módulo Analytics · Dashboard (HU #10243 backend / #10247 frontend).
// Una función por endpoint del contrato `contracts/openapi/core-api.v1.yaml`.
import { apiFetch } from "./client";
import type {
  AnalyticsOverviewParams,
  AnalyticsOverviewResponse,
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

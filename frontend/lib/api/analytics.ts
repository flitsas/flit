// Cliente tipado del módulo Analytics · Dashboard (HU #10243 backend / #10247 frontend).
// Una función por endpoint del contrato `contracts/openapi/core-api.v1.yaml`.
import { apiFetch } from "./client";
import type { AnalyticsOverviewParams, AnalyticsOverviewResponse } from "./types";

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

import { apiFetch } from "@/lib/api/client";

export interface ReportingProcedureRow {
  id: string;
  referenceNumber?: string | null;
  procedureType?: string | null;
  status?: string | null;
  plate?: string | null;
  vin?: string | null;
  transitOfficeName?: string | null;
  companyName?: string | null;
  personDocument?: string | null;
  personName?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  elapsedHoursTotal?: number | null;
}

export interface ReportingProceduresPage {
  items: ReportingProcedureRow[];
  totalCount: number;
  page: number;
  pageSize: number;
  kpis: {
    total: number;
    approved: number;
    rejected: number;
    inProgress: number;
    avgElapsedHours?: number | null;
  };
}

export interface ConsolidadoPage {
  items: Array<{
    dimension: string;
    key: string;
    label: string;
    total: number;
    approved: number;
    rejected: number;
    inProgress: number;
    avgElapsedHours?: number | null;
  }>;
  totalGroups: number;
}

export interface ProductivityPage {
  items: Array<{
    actorId?: string | null;
    actorLabel: string;
    dimension: string;
    total: number;
    approved: number;
    rejected: number;
    inProgress: number;
    avgHours?: number | null;
    minHours?: number | null;
    maxHours?: number | null;
  }>;
}

export interface SlaPage {
  items: Array<{
    procedureType: string;
    transitOfficeName?: string | null;
    slaHours: number;
    total: number;
    withinSla: number;
    outsideSla: number;
    avgBusinessHours?: number | null;
    compliancePct: number;
  }>;
  /** false = tenant sin filas en report_sla_config (AC5 HU #11117). */
  slaConfigured?: boolean;
}

export interface ReportingAudit {
  procedureId: string;
  historyAvailable: boolean;
  entries: Array<{
    changedAt: string;
    fromStatus?: string | null;
    toStatus?: string | null;
    changedByDisplayName?: string | null;
    roleIdAtTime?: string | null;
    organizationIdAtTime?: string | null;
    organizationTypeAtTime?: string | null;
    reason?: string | null;
    historyAvailable: boolean;
  }>;
}

export interface ExportJob {
  id: string;
  status: string;
  reportType: string;
  format: string;
  progressPct: number;
  createdAt: string;
  completedAt?: string | null;
  errorMessage?: string | null;
}

export interface SavedQuery {
  id: string;
  name: string;
  description?: string | null;
  filtersJson: unknown;
  isShared: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface DashboardPreferences {
  configJson: unknown;
}

function qs(params: Record<string, string | number | undefined | null>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === "") continue;
    sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : "";
}

export async function fetchReportingProcedures(params: {
  from?: string;
  to?: string;
  dateType?: string;
  status?: string;
  procedureType?: string;
  transitOfficeId?: string;
  search?: string;
  sortBy?: string;
  sortOrder?: string;
  page?: number;
  pageSize?: number;
  tenantId?: string;
}): Promise<ReportingProceduresPage> {
  return apiFetch(`/api/v1/reporting/procedures${qs(params)}`);
}

export async function fetchConsolidado(params: {
  from?: string;
  to?: string;
  groupBy?: string;
  tenantId?: string;
}): Promise<ConsolidadoPage> {
  return apiFetch(`/api/v1/reporting/consolidado${qs(params)}`);
}

export async function fetchProductivity(params: {
  from?: string;
  to?: string;
  dimension?: string;
  tenantId?: string;
}): Promise<ProductivityPage> {
  return apiFetch(`/api/v1/reporting/productivity${qs(params)}`);
}

export async function fetchSla(params: {
  from?: string;
  to?: string;
  tenantId?: string;
}): Promise<SlaPage> {
  return apiFetch(`/api/v1/reporting/sla${qs(params)}`);
}

export async function fetchProcedureAudit(
  id: string,
  tenantId?: string,
): Promise<ReportingAudit> {
  return apiFetch(`/api/v1/reporting/procedures/${id}/audit${qs({ tenantId })}`);
}

export async function requestExport(body: {
  reportType: string;
  format: string;
  filters?: Record<string, unknown>;
}): Promise<ExportJob> {
  return apiFetch("/api/v1/reporting/exports", {
    method: "POST",
    body,
  });
}

export async function listExports(): Promise<{ items: ExportJob[] }> {
  return apiFetch("/api/v1/reporting/exports");
}

export async function getExport(id: string): Promise<ExportJob> {
  return apiFetch(`/api/v1/reporting/exports/${id}`);
}

export async function getExportDownloadUrl(
  id: string,
): Promise<{ downloadUrl: string; expiresAt: string }> {
  return apiFetch(`/api/v1/reporting/exports/${id}/download-url`);
}

export async function fetchProcedureDetail(
  id: string,
  tenantId?: string,
): Promise<ReportingProcedureRow> {
  return apiFetch(`/api/v1/reporting/procedures/${id}${qs({ tenantId })}`);
}

export async function listSavedQueries(tenantId?: string): Promise<{ items: SavedQuery[] }> {
  return apiFetch(`/api/v1/reporting/saved-queries${qs({ tenantId })}`);
}

export async function createSavedQuery(body: {
  name: string;
  description?: string | null;
  filters?: Record<string, unknown>;
  isShared?: boolean;
  tenantId?: string;
}): Promise<SavedQuery> {
  return apiFetch(`/api/v1/reporting/saved-queries${qs({ tenantId: body.tenantId })}`, {
    method: "POST",
    body: {
      name: body.name,
      description: body.description,
      filters: body.filters ?? {},
      isShared: body.isShared ?? false,
    },
  });
}

export async function updateSavedQuery(
  id: string,
  body: {
    name: string;
    description?: string | null;
    filters?: Record<string, unknown>;
    isShared?: boolean;
    tenantId?: string;
  },
): Promise<SavedQuery> {
  return apiFetch(`/api/v1/reporting/saved-queries/${id}${qs({ tenantId: body.tenantId })}`, {
    method: "PUT",
    body: {
      name: body.name,
      description: body.description,
      filters: body.filters ?? {},
      isShared: body.isShared ?? false,
    },
  });
}

export async function deleteSavedQuery(id: string, tenantId?: string): Promise<void> {
  await apiFetch(`/api/v1/reporting/saved-queries/${id}${qs({ tenantId })}`, {
    method: "DELETE",
  });
}

export async function getDashboardPreferences(
  tenantId?: string,
): Promise<DashboardPreferences> {
  return apiFetch(`/api/v1/reporting/preferences${qs({ tenantId })}`);
}

export async function putDashboardPreferences(
  config: Record<string, unknown>,
  tenantId?: string,
): Promise<DashboardPreferences> {
  return apiFetch(`/api/v1/reporting/preferences${qs({ tenantId })}`, {
    method: "PUT",
    body: { config },
  });
}

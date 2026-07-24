// Cliente del reporte detallado de trámites (Feature #10813).
import { apiFetch } from "./client";
import { downloadFile } from "./download";

export interface DetailedReportFilters {
  from: string;
  to: string;
  tenantId?: string;
  transitOfficeId?: string;
  procedureTypeId?: string;
  category?: string;
  status?: string;
  referenceNumber?: string;
  personDocument?: string;
  personName?: string;
  hasTransformation?: boolean;
  isLeasing?: boolean;
  page?: number;
  pageSize?: number;
}

export interface DetailedProcedureRow {
  id: string;
  referenceNumber: string;
  procedureTypeName: string;
  category: string;
  status: string;
  createdByDisplayName: string;
  submittedAt: string | null;
  completedAt: string | null;
  personDocument: string;
  personFullName: string;
  hasTransformation: boolean;
  transformationDetail: string | null;
  isLeasing: boolean;
  paymentType: string;
  transferType: string;
}

export interface LabelCount {
  label: string;
  count: number;
}

export interface DetailedReportSummary {
  totalCount: number;
  byStatus: { status: string; count: number }[];
  byCategory: LabelCount[];
  byProcedureType: LabelCount[];
}

export interface DetailedReportPage {
  items: DetailedProcedureRow[];
  totalCount: number;
  page: number;
  pageSize: number;
  summary: DetailedReportSummary;
}

const base = "/api/v1/detailed-report";

export function fetchDetailedReport(
  params: DetailedReportFilters,
  signal?: AbortSignal,
): Promise<DetailedReportPage> {
  return apiFetch<DetailedReportPage>(`${base}/procedures`, {
    query: { ...params },
    signal,
  });
}

export function exportDetailedReport(params: DetailedReportFilters): Promise<void> {
  const { page: _p, pageSize: _s, ...rest } = params;
  return downloadFile(`${base}/procedures/export`, {
    query: { ...rest },
    fallbackFilename: `reporte_detallado_${params.from}_${params.to}.xlsx`,
  });
}

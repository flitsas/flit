// Cliente del reporte detallado de trámites (Feature #10813).
import { apiFetch } from "./client";
import type { ActorValidacionDto, MaxActoresPorRol } from "./company-queries";
import { downloadFile } from "./download";

export interface DetailedReportFilters {
  from: string;
  to: string;
  tenantId?: string;
  /** Épica #14296 — SuperAdmin: consulta todas las compañías (sin `tenantId`). */
  allCompanies?: boolean;
  /** Épica #14296 — código de canal. */
  channel?: string;
  /** Épica #14296 — OT del punto; solo con canal `pn_punto`. */
  channelTransitOfficeId?: string;
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
  /** Épica #14296 — ausente en un backend anterior: se lee como «Trámites». */
  channel?: string | null;
  channelTransitOfficeId?: string | null;
  channelTransitOfficeName?: string | null;
  /** Solo con `allCompanies=true` (SuperAdmin). */
  tenantId?: string;
  tenantName?: string;
  /** Validación de identidad por actor. Ausente en un backend anterior: se lee como lista vacía. */
  actores?: ActorValidacionDto[];
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
  maxActoresPorRol?: MaxActoresPorRol;
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

// ── HU #12364 — reporte detallado de la RED (Feature #12257) ─────────────────────────────────────
//
// contrato B4 #12360: `GET /api/v1/tramites/network/reports/procedures` acepta los MISMOS query
// params que `/api/v1/detailed-report/procedures` (salvo `tenantId`, que lo decide el JWT) más
// `childTenantId` opcional; cada fila trae además `tenantId`/`tenantName` del cliente dueño.
// `…/procedures/export` devuelve el XLSX con los mismos params (sin `page`/`pageSize`). 403 si el
// usuario no es cabeza o el hijo es ajeno a la red. Sin `X-Tenant-Id`.

const networkBase = "/api/v1/tramites/network/reports";

/** Filtros del reporte de red: los del propio sin `tenantId` + hijo opcional. */
export type NetworkDetailedReportFilters = Omit<DetailedReportFilters, "tenantId" | "allCompanies"> & {
  /** Acota a un cliente de la red; vacío ⇒ agregado del padre y sus hijos. */
  childTenantId?: string;
};

/** Fila del reporte de red: la fila propia más el cliente dueño del trámite. */
export interface NetworkDetailedProcedureRow extends DetailedProcedureRow {
  tenantId: string;
  tenantName: string;
}

export interface NetworkDetailedReportPage extends Omit<DetailedReportPage, "items"> {
  items: NetworkDetailedProcedureRow[];
}

/** Deja los filtros listos para las rutas de red: nunca viaja `tenantId`, sí `childTenantId`. */
export function toNetworkDetailedReportFilters(
  params: DetailedReportFilters,
  childTenantId?: string,
): NetworkDetailedReportFilters {
  const { tenantId: _t, allCompanies: _a, ...rest } = params;
  return childTenantId ? { ...rest, childTenantId } : rest;
}

// contrato B4 #12360
export function fetchNetworkDetailedReport(
  params: NetworkDetailedReportFilters,
  signal?: AbortSignal,
): Promise<NetworkDetailedReportPage> {
  return apiFetch<NetworkDetailedReportPage>(`${networkBase}/procedures`, {
    query: { ...params },
    signal,
  });
}

// contrato B4 #12360 — AC3: recibe EXACTAMENTE los filtros de la consulta mostrada (sin paginación).
export function exportNetworkDetailedReport(params: NetworkDetailedReportFilters): Promise<void> {
  const { page: _p, pageSize: _s, ...rest } = params;
  return downloadFile(`${networkBase}/procedures/export`, {
    query: { ...rest },
    fallbackFilename: `reporte_detallado_red_${params.from}_${params.to}.xlsx`,
  });
}

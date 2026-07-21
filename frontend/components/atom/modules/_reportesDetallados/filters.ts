import type { DateRange } from "../_reportes/range";

export type TriState = "" | "true" | "false";

export interface DetailedReportFiltersState {
  range: DateRange;
  tenantId?: string;
  transitOfficeId?: string;
  procedureTypeId?: string;
  category?: string;
  status?: string;
  referenceNumber?: string;
  personDocument?: string;
  personName?: string;
  hasTransformation: TriState;
  isLeasing: TriState;
}

export function defaultDetailedFilters(): DetailedReportFiltersState {
  const to = new Date();
  const from = new Date(to);
  from.setDate(from.getDate() - 30);
  return {
    range: { from: from.toISOString().slice(0, 10), to: to.toISOString().slice(0, 10) },
    hasTransformation: "",
    isLeasing: "",
  };
}

export function toQueryParams(
  filters: DetailedReportFiltersState,
  page = 1,
  pageSize = 20,
) {
  return {
    from: filters.range.from,
    to: filters.range.to,
    tenantId: filters.tenantId,
    transitOfficeId: filters.transitOfficeId || undefined,
    procedureTypeId: filters.procedureTypeId || undefined,
    category: filters.category || undefined,
    status: filters.status || undefined,
    referenceNumber: filters.referenceNumber || undefined,
    personDocument: filters.personDocument || undefined,
    personName: filters.personName || undefined,
    hasTransformation: filters.hasTransformation === "" ? undefined : filters.hasTransformation === "true",
    isLeasing: filters.isLeasing === "" ? undefined : filters.isLeasing === "true",
    page,
    pageSize,
  };
}

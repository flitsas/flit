"use client";

// ReportFilterContext — filtros V2 persistidos en URL (HU #11113).
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { toIsoDate } from "./range";

export interface ReportingV2Filters {
  from: string;
  to: string;
  dateType: string;
  status: string;
  procedureType: string;
  tenantId: string;
  transitOfficeId: string;
  search: string;
  sortBy: string;
  sortOrder: "asc" | "desc";
  page: number;
  pageSize: number;
}

const URL_KEYS = [
  "from",
  "to",
  "dateType",
  "status",
  "procedureType",
  "tenantId",
  "transitOfficeId",
  "search",
  "sortBy",
  "sortOrder",
  "page",
  "pageSize",
] as const;

type UrlKey = (typeof URL_KEYS)[number];

function defaultFromTo(reference = new Date()): Pick<ReportingV2Filters, "from" | "to"> {
  const to = toIsoDate(reference);
  const fromDate = new Date(reference);
  fromDate.setDate(fromDate.getDate() - 29);
  return { from: toIsoDate(fromDate), to };
}

export function defaultReportingV2Filters(reference?: Date): ReportingV2Filters {
  const { from, to } = defaultFromTo(reference);
  return {
    from,
    to,
    dateType: "created_at",
    status: "",
    procedureType: "",
    tenantId: "",
    transitOfficeId: "",
    search: "",
    sortBy: "created_at",
    sortOrder: "desc",
    page: 1,
    pageSize: 50,
  };
}

export function parseReportingFiltersFromSearch(
  search: string,
  reference?: Date,
): ReportingV2Filters {
  const params = new URLSearchParams(search.startsWith("?") ? search.slice(1) : search);
  const base = defaultReportingV2Filters(reference);
  const read = (key: UrlKey): string | null => params.get(key);

  const from = read("from") ?? base.from;
  const to = read("to") ?? base.to;
  const sortOrderRaw = read("sortOrder");
  const pageRaw = read("page");
  const pageSizeRaw = read("pageSize");

  return {
    from,
    to,
    dateType: read("dateType") ?? base.dateType,
    status: read("status") ?? "",
    procedureType: read("procedureType") ?? "",
    tenantId: read("tenantId") ?? "",
    transitOfficeId: read("transitOfficeId") ?? "",
    search: read("search") ?? "",
    sortBy: read("sortBy") ?? base.sortBy,
    sortOrder: sortOrderRaw === "asc" ? "asc" : "desc",
    page: Math.max(1, Number.parseInt(pageRaw ?? "", 10) || 1),
    pageSize: Math.min(200, Math.max(1, Number.parseInt(pageSizeRaw ?? "", 10) || 50)),
  };
}

/** Escribe solo las claves de filtro V2; conserva el resto de query params (p. ej. reportesTab). */
export function writeReportingFiltersToUrl(
  filters: ReportingV2Filters,
  defaults: ReportingV2Filters = defaultReportingV2Filters(),
): void {
  if (typeof window === "undefined") return;
  try {
    const url = new URL(window.location.href);
    for (const key of URL_KEYS) {
      const value = filters[key];
      const def = defaults[key];
      const asStr = String(value);
      if (value === "" || value === def || (typeof value === "number" && value === def)) {
        url.searchParams.delete(key);
      } else {
        url.searchParams.set(key, asStr);
      }
    }
    window.history.replaceState(window.history.state, "", url);
  } catch {
    /* SSR / tests sin history */
  }
}

interface ReportFilterContextValue {
  filters: ReportingV2Filters;
  setFilters: (next: ReportingV2Filters | ((prev: ReportingV2Filters) => ReportingV2Filters)) => void;
  patchFilters: (patch: Partial<ReportingV2Filters>) => void;
  resetFilters: () => void;
}

const ReportFilterContext = createContext<ReportFilterContextValue | null>(null);

export function ReportFilterProvider({
  children,
  initialSearch,
}: {
  children: ReactNode;
  /** Inyectable en tests; por defecto `window.location.search`. */
  initialSearch?: string;
}) {
  const [filters, setFiltersState] = useState<ReportingV2Filters>(() => {
    const search =
      initialSearch ??
      (typeof window !== "undefined" ? window.location.search : "");
    return parseReportingFiltersFromSearch(search);
  });

  useEffect(() => {
    writeReportingFiltersToUrl(filters);
  }, [filters]);

  const setFilters = useCallback(
    (next: ReportingV2Filters | ((prev: ReportingV2Filters) => ReportingV2Filters)) => {
      setFiltersState(next);
    },
    [],
  );

  const patchFilters = useCallback((patch: Partial<ReportingV2Filters>) => {
    setFiltersState((prev) => ({ ...prev, ...patch }));
  }, []);

  const resetFilters = useCallback(() => {
    setFiltersState(defaultReportingV2Filters());
  }, []);

  const value = useMemo(
    () => ({ filters, setFilters, patchFilters, resetFilters }),
    [filters, setFilters, patchFilters, resetFilters],
  );

  return (
    <ReportFilterContext.Provider value={value}>{children}</ReportFilterContext.Provider>
  );
}

export function useReportFilters(): ReportFilterContextValue {
  const ctx = useContext(ReportFilterContext);
  if (!ctx) {
    throw new Error("useReportFilters debe usarse dentro de ReportFilterProvider");
  }
  return ctx;
}

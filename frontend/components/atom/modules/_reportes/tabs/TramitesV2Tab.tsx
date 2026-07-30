"use client";

// Tab Trámites V2 — HU #11115: tabla paginada, filtros avanzados, export async, estados UI.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Download, Inbox, Loader2, RefreshCw } from "lucide-react";
import {
  fetchReportingProcedures,
  requestExport,
  type ReportingProceduresPage,
} from "@/lib/api/reporting-v2";
import { ApiError } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";
import { useReportFilters, type ReportingV2Filters } from "../ReportFilterContext";
import { isWithinMaxMonths } from "../range";
import { FLIT_EXPORT_JOB_CREATED } from "../export-events";

const PROCEDURE_TYPE_OPTIONS = [
  { value: "", label: "Todos los tipos" },
  { value: "traslado", label: "Traslado" },
  { value: "matrícula", label: "Matrícula" },
  { value: "traspaso", label: "Traspaso" },
  { value: "radicación", label: "Radicación" },
  { value: "duplicado", label: "Duplicado" },
];

const SORT_OPTIONS = [
  { value: "created_at", label: "Fecha creación" },
  { value: "updated_at", label: "Fecha actualización" },
  { value: "status", label: "Estado" },
  { value: "procedure_type", label: "Tipo" },
  { value: "plate", label: "Placa" },
];

export type FilterChip = { key: keyof ReportingV2Filters | "range"; label: string; clear: Partial<ReportingV2Filters> };

/** Chips de filtros activos (excluye defaults de paginación/sort/rango vacío). */
export function buildActiveFilterChips(
  filters: ReportingV2Filters,
  defaults: Pick<ReportingV2Filters, "from" | "to" | "dateType" | "sortBy" | "sortOrder" | "pageSize">,
): FilterChip[] {
  const chips: FilterChip[] = [];
  if (filters.status) {
    chips.push({ key: "status", label: `Estado: ${filters.status}`, clear: { status: "", page: 1 } });
  }
  if (filters.procedureType) {
    chips.push({
      key: "procedureType",
      label: `Tipo: ${filters.procedureType}`,
      clear: { procedureType: "", page: 1 },
    });
  }
  if (filters.transitOfficeId) {
    chips.push({
      key: "transitOfficeId",
      label: `OT: ${filters.transitOfficeId}`,
      clear: { transitOfficeId: "", page: 1 },
    });
  }
  if (filters.search) {
    chips.push({ key: "search", label: `Búsqueda: ${filters.search}`, clear: { search: "", page: 1 } });
  }
  if (filters.dateType && filters.dateType !== defaults.dateType) {
    chips.push({
      key: "dateType",
      label: `Fecha: ${filters.dateType}`,
      clear: { dateType: defaults.dateType, page: 1 },
    });
  }
  if (filters.from !== defaults.from || filters.to !== defaults.to) {
    chips.push({
      key: "range",
      label: `${filters.from} → ${filters.to}`,
      clear: { from: defaults.from, to: defaults.to, page: 1 },
    });
  }
  if (filters.sortBy !== defaults.sortBy || filters.sortOrder !== defaults.sortOrder) {
    chips.push({
      key: "sortBy",
      label: `Orden: ${filters.sortBy} ${filters.sortOrder}`,
      clear: { sortBy: defaults.sortBy, sortOrder: defaults.sortOrder, page: 1 },
    });
  }
  return chips;
}

export function TramitesV2Tab({ tenantId }: { tenantId?: string }) {
  const { filters, patchFilters } = useReportFilters();
  const { permissions, isSuperAdmin } = usePermissions();
  const canExport = isSuperAdmin || permissions.includes("reporting.export");

  const [data, setData] = useState<ReportingProceduresPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [errorStatus, setErrorStatus] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportMsg, setExportMsg] = useState<string | null>(null);

  const rangeOk = isWithinMaxMonths({ from: filters.from, to: filters.to }, 12);
  const effectiveTenantId = tenantId || filters.tenantId || undefined;

  const defaultsForChips = useMemo(
    () => ({
      from: filters.from,
      to: filters.to,
      dateType: "created_at" as const,
      sortBy: "created_at" as const,
      sortOrder: "desc" as const,
      pageSize: 50,
    }),
    // dateType/sort defaults son constantes de producto; from/to baseline se fija abajo.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  /** Rango al montar (= default 30d o URL) para no chippear el rango por defecto. */
  const [baselineRange] = useState(() => ({ from: filters.from, to: filters.to }));
  const chipDefaults = useMemo(
    () => ({
      ...defaultsForChips,
      from: baselineRange.from,
      to: baselineRange.to,
    }),
    [baselineRange, defaultsForChips],
  );

  const chips = useMemo(
    () => buildActiveFilterChips(filters, chipDefaults),
    [filters, chipDefaults],
  );

  const load = useCallback(async () => {
    if (!rangeOk) {
      setLoading(false);
      setData(null);
      setError(null);
      setErrorStatus(null);
      return;
    }
    setLoading(true);
    setError(null);
    setErrorStatus(null);
    try {
      const pageResult = await fetchReportingProcedures({
        from: filters.from,
        to: filters.to,
        tenantId: effectiveTenantId,
        search: filters.search,
        status: filters.status,
        procedureType: filters.procedureType,
        transitOfficeId: filters.transitOfficeId,
        dateType: filters.dateType,
        sortBy: filters.sortBy,
        sortOrder: filters.sortOrder,
        page: filters.page,
        pageSize: filters.pageSize,
      });
      setData(pageResult);
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        setErrorStatus(err.status);
        setError(err.message || `Error ${err.status}`);
      } else {
        setErrorStatus(null);
        setError(err instanceof Error ? err.message : "Error al cargar trámites");
      }
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [
    rangeOk,
    filters.from,
    filters.to,
    filters.search,
    filters.status,
    filters.procedureType,
    filters.transitOfficeId,
    filters.dateType,
    filters.sortBy,
    filters.sortOrder,
    filters.page,
    filters.pageSize,
    effectiveTenantId,
  ]);

  useEffect(() => {
    void load();
  }, [load, reloadKey]);

  const onExportExcel = async () => {
    if (!canExport || !rangeOk) return;
    setExportBusy(true);
    setExportMsg(null);
    try {
      const job = await requestExport({
        reportType: "procedures",
        format: "excel",
        filters: {
          from: filters.from,
          to: filters.to,
          tenantId: effectiveTenantId,
          search: filters.search,
          status: filters.status,
          procedureType: filters.procedureType,
          transitOfficeId: filters.transitOfficeId,
          dateType: filters.dateType,
        },
      });
      if (typeof window !== "undefined") {
        window.dispatchEvent(new CustomEvent(FLIT_EXPORT_JOB_CREATED, { detail: job }));
      }
      setExportMsg("Exportación solicitada");
    } catch (err: unknown) {
      setExportMsg(err instanceof Error ? err.message : "No se pudo exportar");
    } finally {
      setExportBusy(false);
    }
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="space-y-4" data-testid="tramites-v2-tab">
      <TramitesAdvancedFilters
        filters={filters}
        rangeOk={rangeOk}
        onPatch={(patch) => patchFilters({ ...patch, page: patch.page ?? 1 })}
      />

      {!rangeOk && (
        <p className="text-sm text-amber-700 dark:text-amber-300" data-testid="tramites-range-error" role="alert">
          Rango máximo 12 meses
        </p>
      )}

      {chips.length > 0 && (
        <div className="flex flex-wrap gap-2" data-testid="tramites-filter-chips" aria-label="Filtros activos">
          {chips.map((chip) => (
            <button
              key={String(chip.key)}
              type="button"
              className="inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-[11px] font-medium hover:bg-black/5 dark:hover:bg-white/5"
              onClick={() => patchFilters(chip.clear)}
            >
              {chip.label}
              <span aria-hidden="true">×</span>
            </button>
          ))}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {canExport && (
          <button
            type="button"
            disabled={!rangeOk || exportBusy || loading}
            onClick={() => void onExportExcel()}
            className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold disabled:opacity-50"
            data-testid="tramites-export-excel"
          >
            {exportBusy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
            Exportar
          </button>
        )}
        {exportMsg && <span className="text-[11px] opacity-70">{exportMsg}</span>}
      </div>

      {error && (
        <div
          className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900 dark:bg-red-950/40 dark:text-red-200"
          role="alert"
          data-testid="tramites-error-banner"
        >
          <p>
            {errorStatus != null && <span className="font-semibold">HTTP {errorStatus}: </span>}
            {error}
          </p>
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded-lg border border-red-300 px-3 py-1.5 text-xs font-semibold"
            onClick={() => setReloadKey((k) => k + 1)}
            data-testid="tramites-retry"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Reintentar
          </button>
        </div>
      )}

      {loading && (
        <div className="space-y-2" aria-busy="true" data-testid="tramites-skeleton">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-9 animate-pulse rounded-lg bg-black/10 dark:bg-white/10" />
          ))}
        </div>
      )}

      {!loading && !error && rangeOk && data && data.totalCount === 0 && (
        <div
          className="flex flex-col items-center justify-center gap-3 rounded-2xl border p-10 text-center"
          data-testid="tramites-empty"
        >
          <Inbox className="h-10 w-10 opacity-40" aria-hidden="true" />
          <p className="text-sm font-medium">Sin datos para el período seleccionado</p>
        </div>
      )}

      {!loading && !error && data && data.items.length > 0 && (
        <>
          <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
            <Kpi label="Total" value={data.kpis.total} />
            <Kpi label="Aprobados" value={data.kpis.approved} />
            <Kpi label="Rechazados" value={data.kpis.rejected} />
            <Kpi label="En proceso" value={data.kpis.inProgress} />
            <Kpi
              label="Prom. horas"
              value={data.kpis.avgElapsedHours != null ? data.kpis.avgElapsedHours.toFixed(1) : "—"}
            />
          </div>
          <div className="overflow-x-auto rounded-xl border">
            <table className="min-w-full text-left text-xs" data-testid="tramites-v2-table">
              <thead className="bg-black/5 dark:bg-white/5">
                <tr>
                  <th className="px-3 py-2">Referencia</th>
                  <th className="px-3 py-2">Tipo</th>
                  <th className="px-3 py-2">Estado</th>
                  <th className="px-3 py-2">Placa</th>
                  <th className="px-3 py-2">OT</th>
                  <th className="px-3 py-2">Creado</th>
                </tr>
              </thead>
              <tbody>
                {data.items.slice(0, 50).map((row) => (
                  <tr key={row.id} className="border-t">
                    <td className="px-3 py-2">{row.referenceNumber ?? "—"}</td>
                    <td className="px-3 py-2">{row.procedureType ?? "—"}</td>
                    <td className="px-3 py-2">{row.status ?? "—"}</td>
                    <td className="px-3 py-2">{row.plate || "—"}</td>
                    <td className="px-3 py-2">{row.transitOfficeName || "—"}</td>
                    <td className="px-3 py-2">{new Date(row.createdAt).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="flex flex-wrap items-center justify-between gap-2 text-[11px]">
            <p className="opacity-60">
              {data.totalCount} registros · página {data.page} de {totalPages} · {data.pageSize}/pág
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                className="rounded border px-2 py-1 disabled:opacity-40"
                disabled={filters.page <= 1}
                onClick={() => patchFilters({ page: Math.max(1, filters.page - 1) })}
              >
                Anterior
              </button>
              <button
                type="button"
                className="rounded border px-2 py-1 disabled:opacity-40"
                disabled={filters.page >= totalPages}
                onClick={() => patchFilters({ page: filters.page + 1 })}
              >
                Siguiente
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function TramitesAdvancedFilters({
  filters,
  rangeOk,
  onPatch,
}: {
  filters: ReportingV2Filters;
  rangeOk: boolean;
  onPatch: (patch: Partial<ReportingV2Filters>) => void;
}) {
  return (
    <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4" data-testid="tramites-advanced-filters">
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Desde
        <input
          type="date"
          className={`rounded-lg border px-3 py-2 text-sm ${!rangeOk ? "border-amber-500" : ""}`}
          value={filters.from}
          onChange={(e) => onPatch({ from: e.target.value })}
          aria-label="Fecha desde"
        />
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Hasta
        <input
          type="date"
          className={`rounded-lg border px-3 py-2 text-sm ${!rangeOk ? "border-amber-500" : ""}`}
          value={filters.to}
          onChange={(e) => onPatch({ to: e.target.value })}
          aria-label="Fecha hasta"
        />
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Tipo de fecha
        <select
          className="rounded-lg border px-3 py-2 text-sm"
          value={filters.dateType}
          onChange={(e) => onPatch({ dateType: e.target.value })}
          aria-label="Tipo de fecha"
        >
          <option value="created_at">Creación</option>
          <option value="updated_at">Actualización</option>
          <option value="completed_at">Completado</option>
        </select>
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Estado
        <select
          className="rounded-lg border px-3 py-2 text-sm"
          value={filters.status}
          onChange={(e) => onPatch({ status: e.target.value })}
          aria-label="Filtrar por estado"
          data-testid="tramites-filter-status"
        >
          <option value="">Todos los estados</option>
          <option value="en_proceso">En proceso</option>
          <option value="aprobado">Aprobado</option>
          <option value="rechazado">Rechazado</option>
          <option value="borrador">Borrador</option>
        </select>
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Tipo de trámite
        <select
          className="rounded-lg border px-3 py-2 text-sm"
          value={filters.procedureType}
          onChange={(e) => onPatch({ procedureType: e.target.value })}
          aria-label="Filtrar por tipo"
          data-testid="tramites-filter-procedure-type"
        >
          {PROCEDURE_TYPE_OPTIONS.map((o) => (
            <option key={o.value || "all"} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Organismo de tránsito (ID)
        <input
          className="rounded-lg border px-3 py-2 text-sm"
          value={filters.transitOfficeId}
          onChange={(e) => onPatch({ transitOfficeId: e.target.value })}
          placeholder="UUID OT…"
          aria-label="Filtrar por OT"
        />
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium sm:col-span-2">
        Búsqueda
        <input
          className="rounded-lg border px-3 py-2 text-sm"
          placeholder="Placa, VIN, documento…"
          value={filters.search}
          onChange={(e) => onPatch({ search: e.target.value })}
          aria-label="Buscar trámites"
          data-testid="tramites-filter-search"
        />
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Ordenar por
        <select
          className="rounded-lg border px-3 py-2 text-sm"
          value={filters.sortBy}
          onChange={(e) => onPatch({ sortBy: e.target.value })}
          aria-label="Ordenar por"
        >
          {SORT_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1 text-[11px] font-medium">
        Dirección
        <select
          className="rounded-lg border px-3 py-2 text-sm"
          value={filters.sortOrder}
          onChange={(e) => onPatch({ sortOrder: e.target.value === "asc" ? "asc" : "desc" })}
          aria-label="Dirección de orden"
        >
          <option value="desc">Descendente</option>
          <option value="asc">Ascendente</option>
        </select>
      </label>
    </div>
  );
}

function Kpi({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-xl border p-3">
      <div className="text-[10px] uppercase opacity-60">{label}</div>
      <div className="text-lg font-semibold">{value}</div>
    </div>
  );
}

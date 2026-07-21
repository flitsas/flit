"use client";

import { useCallback, useEffect, useState } from "react";
import { Download } from "lucide-react";
import { usePermissions } from "@/hooks/usePermissions";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { exportDetailedReport, fetchDetailedReport, type DetailedReportPage } from "@/lib/api/detailed-report";
import { tramitesClient } from "@/lib/api/tramites-client";
import { ApiError } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import type { TransitOfficeOption } from "@/lib/api/types/procedure-runtime";
import type { UiStatus } from "@/components/admin/UiStateBoundary";
import { isValidRange } from "../_reportes/range";
import { DetailedReportFiltersPanel } from "./DetailedReportFiltersPanel";
import { DetailedReportGrid } from "./DetailedReportGrid";
import { DetailedReportKpiStrip } from "./DetailedReportKpiStrip";
import { defaultDetailedFilters, toQueryParams, type DetailedReportFiltersState } from "./filters";

export interface DetailedReportPanelProps {
  /** Modo embebido en Trámites (RES-10): oculta padding extra. */
  embedded?: boolean;
}

export function DetailedReportPanel({ embedded = false }: DetailedReportPanelProps) {
  const { isSuperAdmin: isSuper, permissions } = usePermissions();
  const canExport = isSuper || permissions.includes("reportes.detallados.export");

  const [filters, setFilters] = useState<DetailedReportFiltersState>(() => defaultDetailedFilters());
  const [applied, setApplied] = useState<DetailedReportFiltersState>(() => defaultDetailedFilters());
  const [page, setPage] = useState(1);
  const [data, setData] = useState<DetailedReportPage | null>(null);
  const [uiStatus, setUiStatus] = useState<UiStatus>("empty");
  const [errorMessage, setErrorMessage] = useState<string>();
  const [reloadKey, setReloadKey] = useState(0);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeSummary[]>([]);
  const [transitOffices, setTransitOffices] = useState<TransitOfficeOption[]>([]);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    if (!isSuper) return;
    const controller = new AbortController();
    fetchCompaniesIndex({ pageSize: 100, estadoActivo: true }, controller.signal)
      .then((res) => {
        if (!controller.signal.aborted) setCompanies(res.data);
      })
      .catch(() => setCompanies([]));
    return () => controller.abort();
  }, [isSuper]);

  // Catálogo de tipos de trámite (published) para el selector unificado tipo/categoría.
  useEffect(() => {
    let cancelled = false;
    tramitesClient
      .listPublishedProcedureTypes()
      .then((items) => {
        if (!cancelled) setProcedureTypes(items);
      })
      .catch(() => setProcedureTypes([]));
    return () => {
      cancelled = true;
    };
  }, []);

  // Organismos de tránsito habilitados para la compañía consultada. Para SuperAdmin
  // dependen de la empresa elegida; sin empresa no hay OTs que listar.
  useEffect(() => {
    const tenantForOffices = isSuper ? filters.tenantId : undefined;
    if (isSuper && !tenantForOffices) {
      setTransitOffices([]);
      return;
    }
    let cancelled = false;
    tramitesClient
      .listTransitOffices(tenantForOffices || undefined)
      .then((items) => {
        if (!cancelled) setTransitOffices(items);
      })
      .catch(() => setTransitOffices([]));
    return () => {
      cancelled = true;
    };
  }, [isSuper, filters.tenantId]);

  const load = useCallback(async (signal: AbortSignal) => {
    if (!isValidRange(applied.range)) {
      setUiStatus("error");
      setErrorMessage("El rango de fechas no es válido.");
      return;
    }
    if (isSuper && !applied.tenantId) {
      // SuperAdmin sin compañía elegida: el backend exige tenantId. No consultamos (evita el
      // 400) y guiamos a seleccionar una compañía en lugar de mostrar un error.
      setData(null);
      setErrorMessage(undefined);
      setUiStatus("empty");
      return;
    }
    setUiStatus("loading");
    try {
      const res = await fetchDetailedReport(toQueryParams(applied, page, 20), signal);
      if (signal.aborted) return;
      setData(res);
      setUiStatus(res.items.length === 0 ? "empty" : "ready");
    } catch (error) {
      if (signal.aborted || (error as Error).name === "AbortError") return;
      setErrorMessage(describeError(error));
      setUiStatus("error");
    }
  }, [applied, page, reloadKey, isSuper]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  function handleSearch() {
    setApplied(filters);
    setPage(1);
    setReloadKey((k) => k + 1);
  }

  async function handleExport() {
    if (!canExport) return;
    setExporting(true);
    try {
      await exportDetailedReport(toQueryParams(applied, 1, 20));
    } catch {
      setErrorMessage("No se pudo descargar el Excel.");
      setUiStatus("error");
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className={`flex flex-col gap-4 ${embedded ? "" : "p-1"}`}>
      {!embedded && (
        <div className="flex items-center justify-between gap-3">
          <h2 className="text-lg font-bold text-[#162744] dark:text-white">Reportes Detallados</h2>
          {canExport && (
            <button
              type="button"
              onClick={() => void handleExport()}
              disabled={exporting || uiStatus === "loading"}
              className="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: "#557EFF" }}
            >
              <Download className="h-4 w-4" aria-hidden="true" />
              {exporting ? "Descargando…" : "Descargar Excel"}
            </button>
          )}
        </div>
      )}

      <DetailedReportFiltersPanel
        filters={filters}
        onChange={setFilters}
        onSearch={handleSearch}
        isSuper={isSuper}
        companies={companies}
        procedureTypes={procedureTypes}
        transitOffices={transitOffices}
        compact={embedded}
      />

      {embedded && canExport && (
        <button
          type="button"
          onClick={() => void handleExport()}
          disabled={exporting}
          className="self-start inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-xs font-semibold"
        >
          <Download className="h-4 w-4" aria-hidden="true" />
          Descargar Excel
        </button>
      )}

      <DetailedReportKpiStrip summary={data?.summary} />
      <DetailedReportGrid
        data={data}
        uiStatus={uiStatus}
        errorMessage={errorMessage}
        emptyMessage={
          isSuper && !applied.tenantId
            ? "Selecciona una compañía para ver sus trámites."
            : "No hay trámites en el rango de fechas seleccionado."
        }
        onRetry={() => setReloadKey((k) => k + 1)}
        page={page}
        onPageChange={setPage}
      />
    </div>
  );
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return "Revisa el rango de fechas y la compañía seleccionada.";
    if (error.status === 403) return "No tienes permiso para consultar este reporte.";
  }
  return "No se pudo cargar el reporte detallado.";
}

"use client";

import { useEffect, useState } from "react";
import { Download } from "lucide-react";
import { usePermissions } from "@/hooks/usePermissions";
import { useNetworkScope } from "@/hooks/useNetworkScope";
import { NetworkScopeSelector } from "@/components/operacion/NetworkScopeSelector";
import { NetworkScopeBadge } from "@/components/operacion/NetworkScopeBadge";
import { fetchAllCompanies } from "@/lib/api/admin-companies";
import {
  exportDetailedReport,
  exportNetworkDetailedReport,
  fetchDetailedReport,
  fetchNetworkDetailedReport,
  toNetworkDetailedReportFilters,
  type DetailedReportPage,
  type NetworkDetailedReportPage,
} from "@/lib/api/detailed-report";
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

  // HU #12364 — el MISMO selector y la MISMA preferencia (`tramites.scope`) que Trámites (AC5).
  // Con la red activa la consulta y la exportación van a `network/reports/procedures[/export]`
  // con los MISMOS filtros (AC2/AC3); para quien no es cabeza nada cambia (AC4).
  const net = useNetworkScope();
  const networkActive = net.networkActive;
  const networkChildTenantId = net.scope.childTenantId;
  const networkReady = net.ready;

  const [filters, setFilters] = useState<DetailedReportFiltersState>(() => defaultDetailedFilters());
  const [applied, setApplied] = useState<DetailedReportFiltersState>(() => defaultDetailedFilters());
  const [page, setPage] = useState(1);
  const [data, setData] = useState<DetailedReportPage | NetworkDetailedReportPage | null>(null);
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
    fetchAllCompanies({ estadoActivo: true }, controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setCompanies(data);
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
    let cancelled = false;
    async function loadOffices() {
      const tenantForOffices = isSuper ? filters.tenantId : undefined;
      if (isSuper && !tenantForOffices) {
        if (!cancelled) setTransitOffices([]);
        return;
      }
      try {
        const items = await tramitesClient.listTransitOffices(tenantForOffices || undefined);
        if (!cancelled) setTransitOffices(items);
      } catch {
        if (!cancelled) setTransitOffices([]);
      }
    }
    void loadOffices();
    return () => {
      cancelled = true;
    };
  }, [isSuper, filters.tenantId]);

  useEffect(() => {
    const controller = new AbortController();
    async function load() {
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
        const res = networkActive
          ? await fetchNetworkDetailedReport(
              toNetworkDetailedReportFilters(toQueryParams(applied, page, 20), networkChildTenantId),
              controller.signal,
            )
          : await fetchDetailedReport(toQueryParams(applied, page, 20), controller.signal);
        if (controller.signal.aborted) return;
        setData(res);
        setUiStatus(res.items.length === 0 ? "empty" : "ready");
      } catch (error) {
        if (controller.signal.aborted || (error as Error).name === "AbortError") return;
        setErrorMessage(describeError(error));
        setUiStatus("error");
      }
    }
    // La cabeza espera a conocer su alcance guardado para no consultar «lo propio» y luego «la red».
    if (!networkReady) return () => controller.abort();
    void load();
    return () => controller.abort();
    // reloadKey fuerza recarga manual (botón reintentar).
  }, [applied, page, reloadKey, isSuper, networkActive, networkChildTenantId, networkReady]);

  function handleSearch() {
    setApplied(filters);
    setPage(1);
    setReloadKey((k) => k + 1);
  }

  async function handleExport() {
    if (!canExport) return;
    setExporting(true);
    try {
      // AC3 — exactamente los filtros y el alcance de la consulta mostrada (`applied`), nunca los
      // del formulario sin aplicar.
      if (networkActive) {
        await exportNetworkDetailedReport(
          toNetworkDetailedReportFilters(toQueryParams(applied, 1, 20), networkChildTenantId),
        );
      } else {
        await exportDetailedReport(toQueryParams(applied, 1, 20));
      }
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
          <h2 className="text-lg font-bold text-[#162744] dark:text-white flex flex-wrap items-center gap-2">
            Reportes Detallados
            {networkActive && <NetworkScopeBadge scope={net.scope} hijos={net.children} testId="detallado-red-badge" />}
          </h2>
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
        networkScopeSelector={
          net.isGroupParent ? (
            <NetworkScopeSelector
              scope={net.scope}
              onChange={net.setScope}
              hijos={net.children}
              childrenStatus={net.childrenStatus}
              disabled={net.saving}
              testId="detallado-network-scope-select"
            />
          ) : null
        }
      />

      {embedded && networkActive && (
        <NetworkScopeBadge scope={net.scope} hijos={net.children} testId="detallado-red-badge" />
      )}

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
        networkScope={networkActive}
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

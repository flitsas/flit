"use client";

// Tabs Consolidado + Productividad V2 — HU #11116.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Download, Inbox, Loader2, Lock, RefreshCw } from "lucide-react";
import {
  fetchConsolidado,
  fetchProductivity,
  requestExport,
  type ConsolidadoPage,
  type ProductivityPage,
} from "@/lib/api/reporting-v2";
import { ApiError } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";
import { useReportFilters } from "../ReportFilterContext";
import { FLIT_EXPORT_JOB_CREATED } from "../export-events";

/** % de participación sobre el total del consolidado (AC1). */
export function participationPct(rowTotal: number, grandTotal: number): number {
  if (grandTotal <= 0) return 0;
  return Math.round((rowTotal / grandTotal) * 1000) / 10;
}

export function humanizeActorDimension(dimension: string): string {
  switch (dimension) {
    case "usuario":
      return "Usuario / Radicador";
    case "gestor":
      return "Gestor";
    case "ot":
      return "Organismo de tránsito";
    case "empresa":
      return "Empresa";
    default:
      return dimension || "—";
  }
}

export function ConsolidadoTab({ tenantId }: { tenantId?: string }) {
  const { filters } = useReportFilters();
  const { permissions, isSuperAdmin } = usePermissions();
  const canView = isSuperAdmin || permissions.includes("reporting.consolidado");
  const canExport = isSuperAdmin || permissions.includes("reporting.export");

  const [groupBy, setGroupBy] = useState("tipo");
  const [data, setData] = useState<ConsolidadoPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [errorStatus, setErrorStatus] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);
  const [exportBusy, setExportBusy] = useState(false);

  const from = filters.from;
  const to = filters.to;
  const effectiveTenant = tenantId || filters.tenantId || undefined;

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    setErrorStatus(null);
    try {
      const page = await fetchConsolidado({ from, to, groupBy, tenantId: effectiveTenant });
      setData(page);
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        setErrorStatus(err.status);
        setError(err.message);
      } else {
        setErrorStatus(null);
        setError(err instanceof Error ? err.message : "Error al cargar consolidado");
      }
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [canView, from, to, groupBy, effectiveTenant]);

  useEffect(() => {
    void load();
  }, [load, reloadKey]);

  const grandTotal = useMemo(
    () => (data?.items ?? []).reduce((sum, row) => sum + row.total, 0),
    [data],
  );

  const onExportCsv = async () => {
    if (!canExport) return;
    setExportBusy(true);
    try {
      const job = await requestExport({
        reportType: "consolidado",
        format: "csv",
        filters: { from, to, tenantId: effectiveTenant, groupBy },
      });
      if (typeof window !== "undefined") {
        window.dispatchEvent(new CustomEvent(FLIT_EXPORT_JOB_CREATED, { detail: job }));
      }
    } catch {
      /* ExportController / toast global cubren fallos de job */
    } finally {
      setExportBusy(false);
    }
  };

  if (!canView) {
    return (
      <PermissionDenied message="No tienes permiso para ver este reporte" testId="consolidado-no-permiso" />
    );
  }

  return (
    <div className="space-y-3" data-testid="consolidado-tab">
      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-xs font-medium">
          Agrupar por
          <select
            className="rounded border px-2 py-1 text-sm"
            value={groupBy}
            onChange={(e) => setGroupBy(e.target.value)}
            aria-label="Agrupar consolidado por"
            data-testid="consolidado-groupby"
          >
            <option value="tipo">Tipo trámite</option>
            <option value="estado">Estado</option>
            <option value="ot">Organismo</option>
            <option value="empresa">Empresa</option>
            <option value="gestor">Gestor</option>
            <option value="mes">Mes</option>
          </select>
        </label>
        {canExport && (
          <button
            type="button"
            disabled={loading || exportBusy}
            onClick={() => void onExportCsv()}
            className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold disabled:opacity-50"
            data-testid="consolidado-export-csv"
          >
            {exportBusy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
            Exportar
          </button>
        )}
      </div>

      {loading && (
        <div className="space-y-2" aria-busy="true" data-testid="consolidado-loading">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-8 animate-pulse rounded-lg bg-black/10 dark:bg-white/10" />
          ))}
        </div>
      )}

      {!loading && error && (
        <div
          className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800"
          role="alert"
          data-testid="consolidado-error"
        >
          <p>
            {errorStatus != null && <span className="font-semibold">HTTP {errorStatus}: </span>}
            {error}
          </p>
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded-lg border px-3 py-1.5 text-xs font-semibold"
            onClick={() => setReloadKey((k) => k + 1)}
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Reintentar
          </button>
        </div>
      )}

      {!loading && !error && data && data.items.length === 0 && (
        <EmptyState
          message="Sin datos consolidados para el período u OT seleccionados"
          testId="consolidado-empty"
        />
      )}

      {!loading && !error && data && data.items.length > 0 && (
        <div className="overflow-x-auto rounded-xl border" data-testid="consolidado-lleno">
          <table className="min-w-full text-left text-xs">
            <thead className="bg-black/5 dark:bg-white/5">
              <tr>
                <th className="px-3 py-2">Grupo</th>
                <th className="px-3 py-2">Total</th>
                <th className="px-3 py-2">% participación</th>
                <th className="px-3 py-2">Aprobados</th>
                <th className="px-3 py-2">Rechazados</th>
                <th className="px-3 py-2">En proceso</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((row) => (
                <tr key={row.key} className="border-t">
                  <td className="px-3 py-2">{row.label}</td>
                  <td className="px-3 py-2">{row.total}</td>
                  <td className="px-3 py-2">{participationPct(row.total, grandTotal).toFixed(1)}%</td>
                  <td className="px-3 py-2">{row.approved}</td>
                  <td className="px-3 py-2">{row.rejected}</td>
                  <td className="px-3 py-2">{row.inProgress}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export function ProductividadV2Tab({ tenantId }: { tenantId?: string }) {
  const { filters } = useReportFilters();
  const { permissions, isSuperAdmin } = usePermissions();
  const canView = isSuperAdmin || permissions.includes("reporting.productivity");

  const [dimension, setDimension] = useState("usuario");
  const [data, setData] = useState<ProductivityPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [errorStatus, setErrorStatus] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  const from = filters.from;
  const to = filters.to;
  const effectiveTenant = tenantId || filters.tenantId || undefined;

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    setErrorStatus(null);
    try {
      const page = await fetchProductivity({ from, to, dimension, tenantId: effectiveTenant });
      setData(page);
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        setErrorStatus(err.status);
        setError(err.message);
      } else {
        setErrorStatus(null);
        setError(err instanceof Error ? err.message : "Error al cargar productividad");
      }
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [canView, from, to, dimension, effectiveTenant]);

  useEffect(() => {
    void load();
  }, [load, reloadKey]);

  if (!canView) {
    return (
      <PermissionDenied
        message="No tienes permiso para ver este reporte"
        testId="productividad-no-permiso"
      />
    );
  }

  return (
    <div className="space-y-3" data-testid="productividad-tab">
      <label className="flex items-center gap-2 text-xs font-medium">
        Dimensión
        <select
          className="rounded border px-2 py-1 text-sm"
          value={dimension}
          onChange={(e) => setDimension(e.target.value)}
          aria-label="Dimensión de productividad"
          data-testid="productividad-dimension"
        >
          <option value="usuario">Usuario</option>
          <option value="gestor">Gestor</option>
          <option value="ot">OT</option>
          <option value="empresa">Empresa</option>
        </select>
      </label>

      {loading && (
        <div className="space-y-2" aria-busy="true" data-testid="productividad-loading">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-8 animate-pulse rounded-lg bg-black/10 dark:bg-white/10" />
          ))}
        </div>
      )}

      {!loading && error && (
        <div
          className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800"
          role="alert"
          data-testid="productividad-error"
        >
          <p>
            {errorStatus != null && <span className="font-semibold">HTTP {errorStatus}: </span>}
            {error}
          </p>
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded-lg border px-3 py-1.5 text-xs font-semibold"
            onClick={() => setReloadKey((k) => k + 1)}
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Reintentar
          </button>
        </div>
      )}

      {!loading && !error && data && data.items.length === 0 && (
        <EmptyState
          message="Sin datos de productividad para el período u OT seleccionados"
          testId="productividad-empty"
        />
      )}

      {!loading && !error && data && data.items.length > 0 && (
        <div className="overflow-x-auto rounded-xl border" data-testid="productividad-lleno">
          <table className="min-w-full text-left text-xs">
            <thead className="bg-black/5 dark:bg-white/5">
              <tr>
                <th className="px-3 py-2">Actor</th>
                <th className="px-3 py-2">Tipo de actor</th>
                <th className="px-3 py-2">OT</th>
                <th className="px-3 py-2">Total</th>
                <th className="px-3 py-2">Aprob.</th>
                <th className="px-3 py-2">Rech.</th>
                <th className="px-3 py-2">En proceso</th>
                <th className="px-3 py-2">Prom. h</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((row, idx) => (
                <tr key={`${row.actorLabel}-${idx}`} className="border-t">
                  <td className="px-3 py-2">{row.actorLabel}</td>
                  <td className="px-3 py-2">{humanizeActorDimension(row.dimension)}</td>
                  <td className="px-3 py-2">{row.dimension === "ot" ? row.actorLabel : "—"}</td>
                  <td className="px-3 py-2">{row.total}</td>
                  <td className="px-3 py-2">{row.approved}</td>
                  <td className="px-3 py-2">{row.rejected}</td>
                  <td className="px-3 py-2">{row.inProgress}</td>
                  <td className="px-3 py-2">{row.avgHours?.toFixed(1) ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function PermissionDenied({ message, testId }: { message: string; testId: string }) {
  return (
    <div
      className="flex flex-col items-center justify-center gap-3 rounded-2xl border p-10 text-center"
      data-testid={testId}
      role="status"
    >
      <Lock className="h-10 w-10 opacity-40" aria-hidden="true" />
      <p className="text-sm font-medium">{message}</p>
    </div>
  );
}

function EmptyState({ message, testId }: { message: string; testId: string }) {
  return (
    <div
      className="flex flex-col items-center justify-center gap-3 rounded-2xl border p-10 text-center"
      data-testid={testId}
    >
      <Inbox className="h-10 w-10 opacity-40" aria-hidden="true" />
      <p className="text-sm font-medium">{message}</p>
    </div>
  );
}

"use client";

// Pestaña "Operación / Trámites" (Reportes 2.0, HU-C): embudo de estados N03 con
// % de conversión, tabla accionable de trámites atascados, distribución por tipo
// y por OT, y comparativa de KPIs según el periodo comparado (compareWith).
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchFunnel, fetchOtMetrics, variationPct, type StuckItem } from "@/lib/api/analytics-v2";
import type { AnalyticsCategory } from "@/lib/api/types";
import { BarList } from "../BarList";
import { statusLabel } from "../categories";
import { CompanyNotice } from "../CompanyNotice";
import { toMetricsParams, type ReportFilters } from "../filters";
import { formatInt, formatNumber, formatPct } from "../format";
import { FunnelChart } from "../FunnelChart";
import { KpiCard } from "../KpiCard";
import { useAnalyticsQuery } from "../useAnalyticsQuery";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { usePaginacion } from "@/components/atom/usePaginacion";

export interface OperacionTabProps {
  filters: ReportFilters;
  needsCompany: boolean;
  onDrillDown: (segment: { category?: AnalyticsCategory; status?: string }) => void;
}

// HU #13923 — columnas de «Trámites atascados» con el DataTable estándar.
const STUCK_COLUMNS: DataTableColumn<StuckItem>[] = [
  {
    key: "referencia",
    header: "Referencia",
    cellClassName: "font-medium",
    render: (item) => (
      <a
        href={`/tramites/${item.instanceId}`}
        className="text-flit-brand hover:underline"
        aria-label={`Abrir el trámite ${item.referenceNumber}`}
      >
        {item.referenceNumber}
      </a>
    ),
  },
  { key: "estado", header: "Estado", render: (item) => statusLabel(item.status) },
  {
    key: "dias",
    header: "Días detenido",
    cellClassName: "font-semibold text-[#F9AC00]",
    render: (item) => formatNumber(item.daysInStatus),
  },
  { key: "organismo", header: "Organismo", cellClassName: "opacity-80", render: (item) => item.transitOfficeName },
  { key: "tipo", header: "Tipo", cellClassName: "opacity-80", render: (item) => item.procedureTypeName },
  { key: "radicador", header: "Radicador", cellClassName: "opacity-80", render: (item) => item.createdByDisplayName },
];

export function OperacionTab({ filters, needsCompany, onDrillDown }: OperacionTabProps) {
  const pg = usePaginacion("tabs.OperacionTab");
  const params = toMetricsParams(filters);
  const deps = [JSON.stringify(params)];

  const funnel = useAnalyticsQuery((signal) => fetchFunnel(params, signal), deps, {
    skip: needsCompany,
    isEmpty: (res) => res.current.states.every((s) => s.count === 0),
  });
  const metrics = useAnalyticsQuery((signal) => fetchOtMetrics(params, signal), deps, {
    skip: needsCompany,
  });

  if (needsCompany) {
    return <CompanyNotice />;
  }

  const compare = Boolean(filters.compareWith);
  const summary = metrics.data?.current.summary;
  const prevSummary = metrics.data?.previous?.summary ?? null;
  const kpiVariation = (key: "entregados" | "aprobados" | "rechazados" | "rejectionRatePct" | "stuckCount") =>
    compare && summary ? variationPct(summary[key], prevSummary ? prevSummary[key] : null) : undefined;

  return (
    <div className="flex flex-col gap-4">
      {/* KPIs del periodo con variación */}
      <UiStateBoundary
        status={metrics.status}
        errorMessage={metrics.errorMessage}
        onRetry={metrics.retry}
        emptyMessage="Sin métricas de operación para el rango seleccionado."
        skeletonRows={2}
      >
        {summary && (
          <div className="grid grid-cols-2 lg:grid-cols-5 gap-3">
            <KpiCard
              label="Entregados"
              value={formatInt(summary.entregados)}
              tooltip="Transiciones a 'entregado' dentro del rango de fechas."
              variation={kpiVariation("entregados")}
              color="var(--color-flit-brand)"
            />
            <KpiCard
              label="Aprobados"
              value={formatInt(summary.aprobados)}
              tooltip="Transiciones a 'aprobado' dentro del rango de fechas."
              variation={kpiVariation("aprobados")}
              color="#8CC63F"
            />
            <KpiCard
              label="Rechazados"
              value={formatInt(summary.rechazados)}
              tooltip="Transiciones a 'rechazado' dentro del rango de fechas."
              variation={kpiVariation("rechazados")}
              invertVariationColor
              color="#FF4E00"
            />
            <KpiCard
              label="Tasa de rechazo"
              value={formatPct(summary.rejectionRatePct)}
              tooltip="Rechazados / (aprobados + rechazados) × 100 en el rango."
              variation={kpiVariation("rejectionRatePct")}
              invertVariationColor
              color="#FF4E00"
            />
            <KpiCard
              label="Atascados"
              value={formatInt(summary.stuckCount)}
              tooltip="Trámites activos sin cambio de estado hace más días que el umbral (7 por defecto)."
              variation={kpiVariation("stuckCount")}
              invertVariationColor
              color="#F9AC00"
            />
          </div>
        )}
      </UiStateBoundary>

      {/* Embudo de estados */}
      <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="funnel-title">
        <h2
          id="funnel-title"
          className="text-sm font-bold mb-3"
          title="Instancias distintas que alcanzaron cada etapa dentro del rango (por fecha de creación de la instancia)."
        >
          Embudo de estados
        </h2>
        <UiStateBoundary
          status={funnel.status}
          errorMessage={funnel.errorMessage}
          onRetry={funnel.retry}
          emptyMessage="Sin trámites en el periodo para construir el embudo."
          skeletonRows={3}
        >
          {funnel.data && (
            <div className="flex flex-col gap-2">
              <FunnelChart
                states={funnel.data.current.states}
                previousStates={compare ? funnel.data.previous?.states : null}
                onSelectStage={(stage) => onDrillDown({ status: stage })}
              />
              <p className="text-[11px] opacity-70">
                Anulados en el periodo: <b>{formatInt(funnel.data.current.anulados)}</b> · Rechazados vigentes
                (estado actual rechazado): <b>{formatInt(funnel.data.current.rechazadosVigentes)}</b>
              </p>
            </div>
          )}
        </UiStateBoundary>
      </section>

      {/* Atascados accionables + distribución */}
      <UiStateBoundary
        status={metrics.status}
        errorMessage={metrics.errorMessage}
        onRetry={metrics.retry}
        emptyMessage="Sin métricas de operación para el rango seleccionado."
        skeletonRows={3}
      >
        {metrics.data && (
          <div className="flex flex-col gap-4">
            <section aria-labelledby="stuck-title">
              {/* Bug #13055 — tabla homologada con el modelo de trámites (sin tarjeta envolvente). */}
              <h2
                id="stuck-title"
                className="text-sm font-bold mb-3"
                title="Trámites cuyo estado actual no es final y llevan más días detenidos que el umbral configurado."
              >
                Trámites atascados ({formatInt(metrics.data.current.stuck.totalCount)})
              </h2>
              {/* HU #13923 — paginación estándar con «Filas por página»; el texto vacío se conserva. */}
              <div data-testid="stuck-table">
                <DataTable
                  columns={STUCK_COLUMNS}
                  rows={pg.paginar(metrics.data.current.stuck.items)}
                  getRowKey={(item) => item.instanceId}
                  ariaLabel="Trámites atascados"
                  emptyMessage="No hay trámites atascados. ¡Buen ritmo!"
                  pagination={{
                    page: pg.page,
                    pageSize: pg.pageSize,
                    totalCount: metrics.data.current.stuck.items.length,
                    onPageChange: pg.setPage,
                    onPageSizeChange: pg.setPageSize,
                  }}
                />
              </div>
            </section>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
              <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="dist-tipo-title">
                <h2 id="dist-tipo-title" className="text-sm font-bold mb-3" title="Trámites entregados por tipo de trámite en el rango.">
                  Distribución por tipo de trámite
                </h2>
                <BarList
                  items={metrics.data.current.rejectionByType.map((t) => ({
                    key: t.procedureTypeId,
                    label: t.procedureTypeName,
                    value: t.entregados,
                    hint: `${formatInt(t.rechazados)} rechazados (${formatPct(t.rejectionRatePct)})`,
                  }))}
                  color="var(--color-flit-brand)"
                  emptyMessage="Sin entregas por tipo en el periodo."
                />
              </section>
              <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="dist-ot-title">
                <h2 id="dist-ot-title" className="text-sm font-bold mb-3" title="Trámites entregados por organismo de tránsito en el rango.">
                  Distribución por organismo de tránsito
                </h2>
                <BarList
                  items={metrics.data.current.rejectionByOffice.map((o) => ({
                    key: o.transitOfficeId,
                    label: o.transitOfficeName,
                    value: o.entregados,
                    hint: `${formatInt(o.aprobados)} aprobados · ${formatInt(o.rechazados)} rechazados`,
                  }))}
                  color="var(--color-flit-tech)"
                  emptyMessage="Sin entregas por organismo en el periodo."
                />
              </section>
            </div>
          </div>
        )}
      </UiStateBoundary>
    </div>
  );
}

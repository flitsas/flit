"use client";

// Pestaña "Operación / Trámites" (Reportes 2.0, HU-C): embudo de estados N03 con
// % de conversión, tabla accionable de trámites atascados, distribución por tipo
// y por OT, y comparativa de KPIs según el periodo comparado (compareWith).
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchFunnel, fetchOtMetrics, variationPct } from "@/lib/api/analytics-v2";
import type { AnalyticsCategory } from "@/lib/api/types";
import { BarList } from "../BarList";
import { statusLabel } from "../categories";
import { CompanyNotice } from "../CompanyNotice";
import { toMetricsParams, type ReportFilters } from "../filters";
import { formatInt, formatNumber, formatPct } from "../format";
import { FunnelChart } from "../FunnelChart";
import { KpiCard } from "../KpiCard";
import { useAnalyticsQuery } from "../useAnalyticsQuery";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";

export interface OperacionTabProps {
  filters: ReportFilters;
  needsCompany: boolean;
  onDrillDown: (segment: { category?: AnalyticsCategory; status?: string }) => void;
}

export function OperacionTab({ filters, needsCompany, onDrillDown }: OperacionTabProps) {
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
              color="#557EFF"
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
            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="stuck-title">
              <h2
                id="stuck-title"
                className="text-sm font-bold mb-3"
                title="Trámites cuyo estado actual no es final y llevan más días detenidos que el umbral configurado."
              >
                Trámites atascados ({formatInt(metrics.data.current.stuck.totalCount)})
              </h2>
              {metrics.data.current.stuck.items.length === 0 ? (
                <p className="text-xs opacity-60">No hay trámites atascados. ¡Buen ritmo!</p>
              ) : (
                <div className={CARDLIST_SCROLL}>
                  <table className={CARDLIST_TABLE} data-testid="stuck-table">
                    <thead>
                      <tr className={CARDLIST_HEAD_ROW}>
                        <th className={CARDLIST_TH}>Referencia</th>
                        <th className={CARDLIST_TH}>Estado</th>
                        <th className={CARDLIST_TH}>Días detenido</th>
                        <th className={CARDLIST_TH}>Organismo</th>
                        <th className={CARDLIST_TH}>Tipo</th>
                        <th className={CARDLIST_TH}>Radicador</th>
                      </tr>
                    </thead>
                    <tbody>
                      {metrics.data.current.stuck.items.map((item) => (
                        <tr key={item.instanceId} className={CARDLIST_ROW}>
                          <td className={`${CARDLIST_CELL} font-medium`}>
                            <a
                              href={`/tramites/${item.instanceId}`}
                              className="text-[#557EFF] hover:underline"
                              aria-label={`Abrir el trámite ${item.referenceNumber}`}
                            >
                              {item.referenceNumber}
                            </a>
                          </td>
                          <td className={CARDLIST_CELL}>{statusLabel(item.status)}</td>
                          <td className={`${CARDLIST_CELL} font-semibold`} style={{ color: "#F9AC00" }}>
                            {formatNumber(item.daysInStatus)}
                          </td>
                          <td className={`${CARDLIST_CELL} opacity-80`}>{item.transitOfficeName}</td>
                          <td className={`${CARDLIST_CELL} opacity-80`}>{item.procedureTypeName}</td>
                          <td className={`${CARDLIST_CELL} opacity-80`}>{item.createdByDisplayName}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
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
                  color="#557EFF"
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
                  color="#00DBD5"
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

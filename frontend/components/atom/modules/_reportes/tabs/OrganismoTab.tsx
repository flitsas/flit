"use client";

// Pestaña "Organismo de Tránsito" (Reportes 2.0, HU-C): tasa de rechazo por OT y
// por causal, tiempos de aprobación p50/p90 por OT (barras agrupadas Recharts),
// ranking de agilidad y bloque de reincidencia. Todo de GET /ot-metrics.
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchOtMetrics, variationPct } from "@/lib/api/analytics-v2";
import { BarList } from "../BarList";
import { CompanyNotice } from "../CompanyNotice";
import { toMetricsParams, type ReportFilters } from "../filters";
import { formatHours, formatInt, formatNumber, formatPct } from "../format";
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

export interface OrganismoTabProps {
  filters: ReportFilters;
  needsCompany: boolean;
}

export function OrganismoTab({ filters, needsCompany }: OrganismoTabProps) {
  const params = toMetricsParams(filters);
  const metrics = useAnalyticsQuery(
    (signal) => fetchOtMetrics(params, signal),
    [JSON.stringify(params)],
    { skip: needsCompany },
  );

  if (needsCompany) {
    return <CompanyNotice />;
  }

  const compare = Boolean(filters.compareWith);
  const summary = metrics.data?.current.summary;
  const prevSummary = metrics.data?.previous?.summary ?? null;
  const kpiVariation = (key: "rejectionRatePct" | "p50ApprovalHours" | "p90ApprovalHours" | "reincidencePct") =>
    compare && summary ? variationPct(summary[key], prevSummary ? prevSummary[key] : null) : undefined;

  return (
    <UiStateBoundary
      status={metrics.status}
      errorMessage={metrics.errorMessage}
      onRetry={metrics.retry}
      emptyMessage="Sin métricas de organismos de tránsito para el rango seleccionado."
      skeletonRows={4}
    >
      {metrics.data && summary && (
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <KpiCard
              label="Tasa de rechazo"
              value={formatPct(summary.rejectionRatePct)}
              tooltip="Rechazados / (aprobados + rechazados) × 100 en el rango."
              variation={kpiVariation("rejectionRatePct")}
              invertVariationColor
              color="#FF4E00"
            />
            <KpiCard
              label="Aprobación p50"
              value={formatHours(summary.p50ApprovalHours)}
              tooltip="Mediana de horas entre la última entrega y la decisión (aprobado/rechazado)."
              variation={kpiVariation("p50ApprovalHours")}
              invertVariationColor
              color="#557EFF"
            />
            <KpiCard
              label="Aprobación p90"
              value={formatHours(summary.p90ApprovalHours)}
              tooltip="Percentil 90 de horas entre la última entrega y la decisión: el 90% de los trámites se decide en este tiempo o menos."
              variation={kpiVariation("p90ApprovalHours")}
              invertVariationColor
              color="#9B8AFB"
            />
            <KpiCard
              label="Reincidencia"
              value={formatPct(summary.reincidencePct)}
              tooltip="% de trámites rechazados que se reintentaron: reapertura a borrador, re-radicación tras subsanar, o subsanación abierta."
              variation={kpiVariation("reincidencePct")}
              invertVariationColor
              color="#F9AC00"
            />
          </div>

          {/* Partición del ciclo. Hasta ahora solo se medía el tramo del organismo, así que se
              podía señalar al lento pero no corregirse uno mismo. */}
          <section
            className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border"
            aria-labelledby="ciclo-interno-title"
          >
            <h2
              id="ciclo-interno-title"
              className="text-sm font-bold mb-3"
              title="Horas desde que se crea el trámite hasta que se entrega al organismo, frente al tiempo que tarda el organismo en decidir."
            >
              Dónde se va el tiempo
            </h2>
            <BarList
              items={[
                {
                  key: "interno",
                  label: "Nuestro equipo (creación → entrega)",
                  value: metrics.data.current.internalCycle?.p50Hours ?? 0,
                  valueLabel: formatHours(metrics.data.current.internalCycle?.p50Hours ?? null),
                },
                {
                  key: "organismo",
                  label: "El organismo (entrega → decisión)",
                  value: summary.p50ApprovalHours ?? 0,
                  valueLabel: formatHours(summary.p50ApprovalHours),
                },
              ]}
              color="#557EFF"
              emptyMessage="Sin trámites entregados en el periodo."
              testId="ciclo-interno-vs-ot"
            />
            <p className="mt-3 text-[11px] text-[#6B7280] dark:text-white/50">
              Medianas del periodo. La primera barra es la que podemos corregir nosotros.
            </p>
          </section>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="rechazo-ot-title">
              <h2 id="rechazo-ot-title" className="text-sm font-bold mb-3" title="Tasa de rechazo (rechazados / decididos × 100) de cada organismo en el rango.">
                Tasa de rechazo por organismo
              </h2>
              <BarList
                items={metrics.data.current.rejectionByOffice.map((o) => ({
                  key: o.transitOfficeId,
                  label: o.transitOfficeName,
                  value: o.rejectionRatePct,
                  valueLabel: formatPct(o.rejectionRatePct),
                  hint: `${formatInt(o.rechazados)} de ${formatInt(o.aprobados + o.rechazados)} decididos`,
                }))}
                max={100}
                color="#FF4E00"
                emptyMessage="Sin decisiones por organismo en el periodo."
                testId="rechazo-por-ot"
              />
            </section>

            {/* Causales del catálogo si las hay; si no, el texto libre de siempre. La lista
                tipificada es la única agregable: el texto libre devuelve decenas de motivos
                distintos con un caso cada uno porque cada revisor escribe a su manera. */}
            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="rechazo-causal-title">
              {metrics.data.current.rejectionByReasonCatalog?.length ? (
                <>
                  <h2 id="rechazo-causal-title" className="text-sm font-bold mb-3" title="Causales del catálogo marcadas por el organismo al rechazar, dentro del rango.">
                    Rechazos por causal
                  </h2>
                  <BarList
                    items={metrics.data.current.rejectionByReasonCatalog.map((r) => ({
                      key: r.reasonId,
                      label: r.description,
                      value: r.pct,
                      valueLabel: formatPct(r.pct),
                      hint:
                        r.rechazos === 1
                          ? "1 rechazo la incluye"
                          : `${formatInt(r.rechazos)} rechazos la incluyen`,
                    }))}
                    max={100}
                    color="#F9AC00"
                    emptyMessage="Sin causales de rechazo registradas en el periodo."
                    testId="rechazo-por-causal"
                  />
                  {/* Un rechazo puede llevar varias causales: leído como reparto, el % engaña. */}
                  <p className="mt-3 text-[11px] text-[#6B7280] dark:text-white/50">
                    % de rechazos que incluyen cada causal; como un rechazo puede tener varias, la
                    suma puede pasar del 100 %. Promedio de{" "}
                    {metrics.data.current.avgReasonsPerRejection} causales por rechazo.
                  </p>
                </>
              ) : (
                <>
                  <h2 id="rechazo-causal-title" className="text-sm font-bold mb-3" title="Motivos escritos a mano en las transiciones a 'rechazado' dentro del rango.">
                    Rechazos por causal
                  </h2>
                  <BarList
                    items={metrics.data.current.rejectionByReason.map((r) => ({
                      key: r.reason,
                      label: r.reason,
                      value: r.count,
                      hint: `${formatPct(r.pct)} de los rechazos`,
                    }))}
                    color="#F9AC00"
                    emptyMessage="Sin causales de rechazo registradas en el periodo."
                    testId="rechazo-por-causal"
                  />
                  <p className="mt-3 text-[11px] text-[#6B7280] dark:text-white/50">
                    Motivos escritos a mano: no son agrupables. Se verán agrupados a medida que los
                    organismos rechacen con las causales del catálogo.
                  </p>
                </>
              )}
            </section>
          </div>

          <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="tiempos-ot-title">
            <h2 id="tiempos-ot-title" className="text-sm font-bold mb-3" title="Mediana (p50) y percentil 90 (p90) de horas hasta la decisión, por organismo.">
              Tiempos de aprobación por organismo (p50 / p90)
            </h2>
            {metrics.data.current.approvalTimesByOffice.length === 0 ? (
              <p className="text-xs opacity-60">Sin decisiones en el periodo.</p>
            ) : (
              <div className="h-64" data-testid="tiempos-aprobacion-chart">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={[...metrics.data.current.approvalTimesByOffice]} margin={{ left: 0, right: 16, top: 8, bottom: 4 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke="#DFE5ED" />
                    <XAxis dataKey="transitOfficeName" tick={{ fontSize: 10 }} interval={0} />
                    <YAxis tick={{ fontSize: 11 }} width={40} unit=" h" />
                    <Tooltip
                      formatter={(value: number, name: string) => [formatHours(value), name]}
                      contentStyle={{ background: "rgba(22,39,68,0.95)", border: "none", borderRadius: 10, color: "#fff", fontSize: 11 }}
                    />
                    <Legend wrapperStyle={{ fontSize: 11 }} />
                    <Bar dataKey="p50Hours" name="p50 (mediana)" fill="#557EFF" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                    <Bar dataKey="p90Hours" name="p90" fill="#9B8AFB" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}
          </section>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="ranking-title">
              <h2 id="ranking-title" className="text-sm font-bold mb-3" title="Orden por mediana de tiempo de decisión ascendente; desempata la tasa de rechazo.">
                Ranking de agilidad
              </h2>
              {metrics.data.current.officeRanking.length === 0 ? (
                <p className="text-xs opacity-60">Sin organismos con decisiones en el periodo.</p>
              ) : (
                <div className={CARDLIST_SCROLL}>
                  <table className={CARDLIST_TABLE} data-testid="ranking-ot">
                    <thead>
                      <tr className={CARDLIST_HEAD_ROW}>
                        <th className={CARDLIST_TH}>#</th>
                        <th className={CARDLIST_TH}>Organismo</th>
                        <th className={CARDLIST_TH}>p50</th>
                        <th className={CARDLIST_TH}>Rechazo</th>
                        <th className={CARDLIST_TH}>Volumen</th>
                      </tr>
                    </thead>
                    <tbody>
                      {metrics.data.current.officeRanking.map((o) => (
                        <tr key={o.transitOfficeId} className={CARDLIST_ROW}>
                          <td className={`${CARDLIST_CELL} font-bold`} style={{ color: o.rank <= 3 ? "#8CC63F" : undefined }}>
                            {o.rank}
                          </td>
                          <td className={`${CARDLIST_CELL} font-medium`}>{o.transitOfficeName}</td>
                          <td className={CARDLIST_CELL}>{formatHours(o.p50Hours)}</td>
                          <td className={CARDLIST_CELL}>{formatPct(o.rejectionRatePct)}</td>
                          <td className={`${CARDLIST_CELL} opacity-80`}>{formatInt(o.volumen)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="reincidencia-title">
              <h2 id="reincidencia-title" className="text-sm font-bold mb-3" title="Trámites rechazados que volvieron a borrador; ciclos = veces que pasaron por 'rechazado'.">
                Reincidencia
              </h2>
              <div className="grid grid-cols-2 gap-3" data-testid="reincidencia-bloque">
                <Stat label="Rechazadas" value={formatInt(metrics.data.current.reincidence.rechazadas)} color="#FF4E00" tooltip="Instancias con al menos un rechazo en el rango." />
                <Stat label="Reintentadas" value={formatInt(metrics.data.current.reincidence.reintentadas)} color="#557EFF" tooltip="Rechazadas que volvieron a borrador para corregirse." />
                <Stat label="Ciclos promedio" value={formatNumber(metrics.data.current.reincidence.avgCiclos)} color="#F9AC00" tooltip="Promedio de veces que una instancia pasó por 'rechazado'." />
                <Stat label="Ciclos máximo" value={formatInt(metrics.data.current.reincidence.maxCiclos)} color="#9B8AFB" tooltip="Máximo de pasadas por 'rechazado' de una misma instancia." />
              </div>
            </section>
          </div>
        </div>
      )}
    </UiStateBoundary>
  );
}

function Stat({ label, value, color, tooltip }: { label: string; value: string; color: string; tooltip: string }) {
  return (
    <div className="rounded-xl border p-3" title={tooltip}>
      <p className="text-[10px] opacity-70 font-medium">{label}</p>
      <p className="text-xl font-bold mt-0.5" style={{ color }}>
        {value}
      </p>
    </div>
  );
}

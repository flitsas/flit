"use client";

// Pestaña "Productividad" (Reportes 2.0, HU-C): tarjetas Top 5 existentes
// (recolocadas), tabla de detalle por operador (fetchTopProducers con límite alto)
// y comparativa entre operadores con barras agrupadas (Recharts).
import { useMemo } from "react";
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchTopProducers } from "@/lib/api/analytics";
import { toMetricsParams, type ReportFilters } from "../filters";
import { formatInt, formatPct } from "../format";
import { ProductivityCards } from "../ProductivityCards";
import { useAnalyticsQuery } from "../useAnalyticsQuery";

const DETAIL_LIMIT = 100;
const CHART_LIMIT = 10;

export interface ProductividadTabProps {
  filters: ReportFilters;
}

export function ProductividadTab({ filters }: ProductividadTabProps) {
  const params = toMetricsParams(filters);
  const producers = useAnalyticsQuery(
    (signal) =>
      fetchTopProducers(
        { from: params.from, to: params.to, limit: DETAIL_LIMIT, tenantId: params.tenantId },
        signal,
      ),
    [params.from, params.to, params.tenantId],
    { isEmpty: (res) => res.items.length === 0 },
  );

  const items = useMemo(() => producers.data?.items ?? [], [producers.data]);
  const chartData = useMemo(
    () =>
      items.slice(0, CHART_LIMIT).map((p) => ({
        name: p.displayName,
        Enviados: p.submittedCount,
        Aprobados: p.approvedCount,
        Rechazados: p.rejectedCount,
      })),
    [items],
  );

  return (
    <div className="flex flex-col gap-4">
      {/* Tarjetas Top 5 existentes (multiselect) */}
      <ProductivityCards
        producers={items.slice(0, 5)}
        status={producers.status}
        errorMessage={producers.errorMessage}
        onRetry={producers.retry}
      />

      <UiStateBoundary
        status={producers.status}
        errorMessage={producers.errorMessage}
        onRetry={producers.retry}
        emptyMessage="No hay productividad para el periodo seleccionado."
        skeletonRows={3}
      >
        <div className="flex flex-col gap-4">
          <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="comparativa-title">
            <h2
              id="comparativa-title"
              className="text-sm font-bold mb-3"
              title={`Trámites enviados, aprobados y rechazados por operador (top ${CHART_LIMIT}).`}
            >
              Comparativa entre operadores
            </h2>
            <div className="h-64" data-testid="comparativa-operadores-chart">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={chartData} margin={{ left: 0, right: 16, top: 8, bottom: 4 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#DFE5ED" />
                  <XAxis dataKey="name" tick={{ fontSize: 10 }} interval={0} />
                  <YAxis tick={{ fontSize: 11 }} allowDecimals={false} width={36} />
                  <Tooltip
                    contentStyle={{ background: "rgba(22,39,68,0.95)", border: "none", borderRadius: 10, color: "#fff", fontSize: 11 }}
                  />
                  <Legend wrapperStyle={{ fontSize: 11 }} />
                  <Bar dataKey="Enviados" fill="#557EFF" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                  <Bar dataKey="Aprobados" fill="#8CC63F" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                  <Bar dataKey="Rechazados" fill="#FF4E00" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </section>

          <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="detalle-operadores-title">
            <h2
              id="detalle-operadores-title"
              className="text-sm font-bold mb-3"
              title="Todos los operadores con actividad en el rango, ordenados por trámites enviados."
            >
              Detalle por operador ({formatInt(items.length)})
            </h2>
            <div className="overflow-x-auto">
              <table className="w-full text-xs" data-testid="operadores-table">
                <thead>
                  <tr className="text-[10px] uppercase text-left opacity-70">
                    <th className="px-2 py-2 font-semibold">#</th>
                    <th className="px-2 py-2 font-semibold">Operador</th>
                    <th className="px-2 py-2 font-semibold">Enviados</th>
                    <th className="px-2 py-2 font-semibold">Aprobados</th>
                    <th className="px-2 py-2 font-semibold">Rechazados</th>
                    <th className="px-2 py-2 font-semibold">% aprobación</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((p, i) => {
                    const decided = p.approvedCount + p.rejectedCount;
                    const approvalPct = decided > 0 ? (p.approvedCount / decided) * 100 : null;
                    return (
                      <tr key={p.userId} className="border-t">
                        <td className="px-2 py-2 opacity-60">{i + 1}</td>
                        <td className="px-2 py-2 font-medium">{p.displayName}</td>
                        <td className="px-2 py-2">{formatInt(p.submittedCount)}</td>
                        <td className="px-2 py-2" style={{ color: "#8CC63F" }}>{formatInt(p.approvedCount)}</td>
                        <td className="px-2 py-2" style={{ color: p.rejectedCount > 0 ? "#FF4E00" : undefined }}>
                          {formatInt(p.rejectedCount)}
                        </td>
                        <td
                          className="px-2 py-2"
                          title="Aprobados / (aprobados + rechazados) × 100; — si aún no hay decisiones."
                        >
                          {formatPct(approvalPct)}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      </UiStateBoundary>
    </div>
  );
}

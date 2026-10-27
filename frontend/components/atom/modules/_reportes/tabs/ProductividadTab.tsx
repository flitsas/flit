"use client";

// Pestaña "Productividad" (Reportes 2.0, HU-C): tarjetas Top 5 existentes
// (recolocadas), tabla de detalle por operador (fetchTopProducers con límite alto)
// y comparativa entre operadores con barras agrupadas (Recharts).
import { useMemo } from "react";
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchNetworkTopProducers, fetchTopProducers } from "@/lib/api/analytics";
import type { TopProducer } from "@/lib/api/types";
import type { NetworkChildOption } from "@/hooks/useNetworkScope";
import { NetworkScopeBadge } from "@/components/operacion/NetworkScopeBadge";
import type { NetworkScopePreference } from "@/lib/tramites/network-scope";
import { toMetricsParams, type ReportFilters } from "../filters";
import { formatInt, formatPct } from "../format";
import { ProductivityCards } from "../ProductivityCards";
import { useAnalyticsQuery } from "../useAnalyticsQuery";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";

// HU #13923 — fila del detalle por operador: el número de orden depende de la página, así que se
// calcula al paginar y viaja con la fila.
type OperadorFila = TopProducer & { n: number };

const OPERADOR_COLUMNS: DataTableColumn<OperadorFila>[] = [
  { key: "n", header: "#", cellClassName: "opacity-60", render: (p) => p.n },
  { key: "operador", header: "Operador", cellClassName: "font-medium", render: (p) => p.displayName },
  { key: "enviados", header: "Enviados", render: (p) => formatInt(p.submittedCount) },
  {
    key: "aprobados",
    header: "Aprobados",
    render: (p) => <span style={{ color: "#8CC63F" }}>{formatInt(p.approvedCount)}</span>,
  },
  {
    key: "rechazados",
    header: "Rechazados",
    render: (p) => <span style={{ color: p.rejectedCount > 0 ? "#FF4E00" : undefined }}>{formatInt(p.rejectedCount)}</span>,
  },
  {
    key: "aprobacion",
    header: "% aprobación",
    render: (p) => {
      const decided = p.approvedCount + p.rejectedCount;
      const approvalPct = decided > 0 ? (p.approvedCount / decided) * 100 : null;
      return (
        <span title="Aprobados / (aprobados + rechazados) × 100; — si aún no hay decisiones.">
          {formatPct(approvalPct)}
        </span>
      );
    },
  },
];

const DETAIL_LIMIT = 100;
const CHART_LIMIT = 10;

export interface ProductividadTabProps {
  filters: ReportFilters;
  /** HU #12364 — alcance de red vigente: el Top va a `network/stats/productivity/top` (AC1). */
  networkScope?: NetworkScopePreference;
  networkChildren?: readonly NetworkChildOption[];
}

export function ProductividadTab({ filters, networkScope, networkChildren = [] }: ProductividadTabProps) {
  const params = toMetricsParams(filters);
  const networkActive = networkScope?.mode === "network";
  const childTenantId = networkActive ? networkScope?.childTenantId : undefined;
  const producers = useAnalyticsQuery(
    (signal) =>
      networkActive
        ? fetchNetworkTopProducers({ from: params.from, to: params.to, limit: DETAIL_LIMIT, childTenantId }, signal)
        : fetchTopProducers(
            { from: params.from, to: params.to, limit: DETAIL_LIMIT, tenantId: params.tenantId },
            signal,
          ),
    [params.from, params.to, params.tenantId, networkActive, childTenantId],
    { isEmpty: (res) => res.items.length === 0 },
  );

  const items = useMemo(() => producers.data?.items ?? [], [producers.data]);
  // Bug #13055 — el detalle por operador puede llegar a 100 filas: paginación en cliente.
  const pg = usePaginacion("tabs.ProductividadTab");
  const ultimaPagina = Math.max(1, Math.ceil(items.length / pg.pageSize));
  const paginaActual = Math.min(pg.page, ultimaPagina);
  const inicioPagina = (paginaActual - 1) * pg.pageSize;
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
      {networkScope && networkActive && (
        <NetworkScopeBadge scope={networkScope} hijos={networkChildren} testId="productividad-red-badge" />
      )}
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
                  <Bar dataKey="Enviados" fill="var(--color-flit-brand)" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                  <Bar dataKey="Aprobados" fill="#8CC63F" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                  <Bar dataKey="Rechazados" fill="#FF4E00" radius={[4, 4, 0, 0]} isAnimationActive={false} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </section>

          <section aria-labelledby="detalle-operadores-title">
            <h2
              id="detalle-operadores-title"
              className="text-sm font-bold mb-3"
              title="Todos los operadores con actividad en el rango, ordenados por trámites enviados."
            >
              Detalle por operador ({formatInt(items.length)})
            </h2>
            {/* Bug #13055 — tabla homologada con el modelo de trámites (sin tarjeta envolvente). */}
            <div data-testid="operadores-table">
              <DataTable
                columns={OPERADOR_COLUMNS}
                rows={pg.paginar(items).map((p, i) => ({ ...p, n: inicioPagina + i + 1 }))}
                getRowKey={(p) => p.userId}
                ariaLabel="Detalle por operador"
                emptyMessage="Sin operadores con actividad en el periodo."
                pagination={{
                  page: paginaActual,
                  pageSize: pg.pageSize,
                  totalCount: items.length,
                  onPageChange: pg.setPage,
                  onPageSizeChange: pg.setPageSize,
                }}
              />
            </div>
          </section>
        </div>
      </UiStateBoundary>
    </div>
  );
}

"use client";

// Pestaña "Resumen general" (Reportes 2.0, HU-C): KPIs grandes con variación,
// donuts por categoría con drill-down (recolocados del dashboard original),
// tendencia mensual (LineChart · primer consumidor de fetchMonthlyTrend) y
// panel "Ahora mismo" con auto-refresh.
import { useMemo } from "react";
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchAnalyticsOverview, fetchMonthlyTrend } from "@/lib/api/analytics";
import { variationPct } from "@/lib/api/analytics-v2";
import type {
  AnalyticsCategory,
  AnalyticsOverviewResponse,
  CategoryMetrics,
  MonthlyTrendResponse,
} from "@/lib/api/types";
import { CategoryDonut } from "../CategoryDonut";
import { CATEGORY_META, CATEGORY_ORDER } from "../categories";
import { compareRange, type ReportFilters } from "../filters";
import { formatInt } from "../format";
import { KpiCard } from "../KpiCard";
import { LiveNowPanel } from "../LiveNowPanel";
import { useAnalyticsQuery } from "../useAnalyticsQuery";
import type { DashboardPreferencesConfig } from "../dashboardPreferences";

export interface ResumenTabProps {
  filters: ReportFilters;
  /** SuperAdmin sin compañía: los endpoints NUEVOS no se llaman (los legados sí). */
  needsCompany: boolean;
  onDrillDown: (segment: { category?: AnalyticsCategory; status?: string }) => void;
  /** `category:status` abierto en el panel de detalle, para resaltar la leyenda. */
  activeSegmentKey?: string;
  /** Preferencias de visibilidad/orden de KPIs (HU #11118). */
  kpiPreferences?: DashboardPreferencesConfig | null;
}

/** Completa las categorías ausentes con totales en cero para pintar siempre los donuts. */
function normalizeCategories(data: AnalyticsOverviewResponse | null): Record<AnalyticsCategory, CategoryMetrics> {
  const base: Record<AnalyticsCategory, CategoryMetrics> = {
    matriculas: { category: "matriculas", total: 0, byStatus: [] },
    traspasos: { category: "traspasos", total: 0, byStatus: [] },
    vehicular: { category: "vehicular", total: 0, byStatus: [] },
    otros: { category: "otros", total: 0, byStatus: [] },
  };
  for (const cat of data?.categories ?? []) {
    base[cat.category] = cat;
  }
  return base;
}

function categoryTotals(data: AnalyticsOverviewResponse | null): Record<AnalyticsCategory, number> & { total: number } {
  const byCat = normalizeCategories(data);
  const totals = {
    matriculas: byCat.matriculas.total,
    traspasos: byCat.traspasos.total,
    vehicular: byCat.vehicular.total,
    otros: byCat.otros.total,
    total: 0,
  };
  totals.total = CATEGORY_ORDER.reduce((acc, c) => acc + byCat[c].total, 0);
  return totals;
}

export function ResumenTab({
  filters,
  needsCompany,
  onDrillDown,
  activeSegmentKey,
  kpiPreferences,
}: ResumenTabProps) {
  const { range, tenantId, compareWith } = filters;
  const params = { from: range.from, to: range.to, tenantId: tenantId || undefined };

  const isKpiVisible = (id: string) =>
    kpiPreferences?.kpis.find((k) => k.id === id)?.visible !== false;

  // Overview (endpoint legado): admite SuperAdmin sin tenant (vista global).
  const overview = useAnalyticsQuery(
    (signal) => fetchAnalyticsOverview(params, signal),
    [range.from, range.to, tenantId],
    { isEmpty: (res) => !res.categories.some((c) => c.total > 0) },
  );

  // Ventana comparada calculada en cliente (el overview legado no soporta compareWith).
  const prevRange = compareWith ? compareRange(range, compareWith) : null;
  const previousOverview = useAnalyticsQuery(
    (signal) =>
      fetchAnalyticsOverview({ from: prevRange!.from, to: prevRange!.to, tenantId: tenantId || undefined }, signal),
    [prevRange?.from, prevRange?.to, tenantId],
    { skip: !prevRange },
  );

  // Tendencia mensual (fetchMonthlyTrend estrena consumidor).
  const trend = useAnalyticsQuery(
    (signal) => fetchMonthlyTrend(params, signal),
    [range.from, range.to, tenantId],
    { isEmpty: (res) => res.items.length === 0 },
  );

  const byCategory = useMemo(() => normalizeCategories(overview.data), [overview.data]);
  const totals = useMemo(() => categoryTotals(overview.data), [overview.data]);
  const prevTotals = useMemo(
    () => (previousOverview.data ? categoryTotals(previousOverview.data) : null),
    [previousOverview.data],
  );
  const trendRows = useMemo(() => buildTrendRows(trend.data), [trend.data]);

  const compare = Boolean(compareWith);
  const variation = (key: AnalyticsCategory | "total"): number | null | undefined =>
    compare ? variationPct(totals[key], prevTotals ? prevTotals[key] : null) : undefined;

  return (
    <div className="flex flex-col gap-4">
      <UiStateBoundary
        status={overview.status}
        errorMessage={overview.errorMessage}
        onRetry={overview.retry}
        emptyMessage="No hay trámites para el rango de fechas seleccionado."
        skeletonRows={3}
      >
        <div className="flex flex-col gap-4">
          {/* KPIs grandes con variación y tooltip "cómo se calcula" */}
          <div className="grid grid-cols-2 lg:grid-cols-5 gap-3">
            {isKpiVisible("totalTramites") && (
              <KpiCard
                label="Total trámites"
                value={formatInt(totals.total)}
                tooltip="Suma de trámites de todas las categorías creados en el rango de fechas."
                variation={variation("total")}
                color="#162744"
                testId="kpi-total-tramites"
              />
            )}
            {CATEGORY_ORDER.filter((cat) => isKpiVisible(cat)).map((cat) => (
              <KpiCard
                key={cat}
                label={CATEGORY_META[cat].label}
                value={formatInt(totals[cat])}
                tooltip={`Trámites de la categoría ${CATEGORY_META[cat].label} en el rango de fechas.`}
                variation={variation(cat)}
                color={CATEGORY_META[cat].color}
              />
            ))}
            {isKpiVisible("tramitesRechazados") && (
              <KpiCard
                label="Trámites rechazados"
                value={formatInt(
                  (overview.data?.categories ?? []).reduce(
                    (acc, cat) =>
                      acc +
                      (cat.byStatus.find((s) => s.status === "rechazado")?.count ?? 0),
                    0,
                  ),
                )}
                tooltip="Suma de trámites en estado rechazado en el rango."
                invertVariationColor
                testId="kpi-tramites-rechazados"
              />
            )}
          </div>

          {/* Donuts por categoría (drill-down al detalle) */}
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-3">
            {CATEGORY_ORDER.map((cat) => (
              <CategoryDonut
                key={cat}
                metrics={byCategory[cat]}
                onSelect={(category, status) => onDrillDown({ category, status })}
                activeKey={activeSegmentKey}
              />
            ))}
          </div>
        </div>
      </UiStateBoundary>

      {/* Tendencia mensual */}
      <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="tendencia-title">
        <h2 id="tendencia-title" className="text-sm font-bold mb-3" title="Trámites creados por mes y categoría dentro del rango.">
          Tendencia mensual
        </h2>
        <UiStateBoundary
          status={trend.status}
          errorMessage={trend.errorMessage}
          onRetry={trend.retry}
          emptyMessage="Sin datos de tendencia para el rango seleccionado."
          skeletonRows={2}
        >
          <div className="h-64" data-testid="tendencia-mensual-chart">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={trendRows} margin={{ left: 0, right: 16, top: 8, bottom: 4 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#DFE5ED" />
                <XAxis dataKey="label" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} allowDecimals={false} width={36} />
                <Tooltip
                  contentStyle={{ background: "rgba(22,39,68,0.95)", border: "none", borderRadius: 10, color: "#fff", fontSize: 11 }}
                />
                <Legend wrapperStyle={{ fontSize: 11 }} />
                {CATEGORY_ORDER.map((cat) => (
                  <Line
                    key={cat}
                    type="monotone"
                    dataKey={cat}
                    name={CATEGORY_META[cat].label}
                    stroke={CATEGORY_META[cat].color}
                    strokeWidth={2}
                    dot={false}
                    isAnimationActive={false}
                  />
                ))}
              </LineChart>
            </ResponsiveContainer>
          </div>
        </UiStateBoundary>
      </section>

      {/* Panel en tiempo real (endpoint nuevo → SuperAdmin necesita compañía) */}
      <LiveNowPanel
        tenantId={tenantId || undefined}
        skip={needsCompany}
        onDrillDown={(status) => onDrillDown({ status })}
      />
    </div>
  );
}

interface TrendRow {
  label: string;
  matriculas: number;
  traspasos: number;
  vehicular: number;
  otros: number;
}

function buildTrendRows(data: MonthlyTrendResponse | null): TrendRow[] {
  if (!data) return [];
  const byMonth = new Map<string, TrendRow>();
  for (const point of data.items) {
    const key = `${point.year}-${String(point.month).padStart(2, "0")}`;
    let row = byMonth.get(key);
    if (!row) {
      row = { label: key, matriculas: 0, traspasos: 0, vehicular: 0, otros: 0 };
      byMonth.set(key, row);
    }
    row[point.category] += point.total;
  }
  return Array.from(byMonth.entries())
    .sort(([a], [b]) => (a < b ? -1 : 1))
    .map(([, row]) => row);
}

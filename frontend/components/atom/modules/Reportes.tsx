"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { ModuleTitle } from "./ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchAnalyticsOverview, fetchTopProducers } from "@/lib/api/analytics";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { ApiError } from "@/lib/api/types";
import type {
  AnalyticsCategory,
  AnalyticsOverviewResponse,
  CategoryMetrics,
  CompanyListItem,
  TopProducer,
} from "@/lib/api/types";
import { DateRangeFilter } from "./_reportes/DateRangeFilter";
import { CompanySelector } from "./_reportes/CompanySelector";
import { CategoryDonut } from "./_reportes/CategoryDonut";
import { ProductivityCards } from "./_reportes/ProductivityCards";
import { ProcedureDetailPanel } from "./_reportes/ProcedureDetailPanel";
import { ExportButtons } from "./_reportes/ExportButtons";
import { CATEGORY_META, CATEGORY_ORDER } from "./_reportes/categories";
import { defaultRange, isValidRange, type DateRange } from "./_reportes/range";

/** Segmento seleccionado en un donut → abre el detalle lateral (HU #10248, AC1). */
interface SelectedSegment {
  category: AnalyticsCategory;
  status?: string;
}

/** Traduce un fallo de la API a un mensaje accionable para el usuario (AC3). */
function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 400) return "El rango de fechas no es válido o falta la compañía.";
    if (error.status === 403) return "No tienes acceso a las métricas de esa compañía.";
    if (error.status === 401) return "Tu sesión expiró. Vuelve a iniciar sesión.";
  }
  return "No se pudieron cargar las métricas del dashboard.";
}

/** Completa las categorías ausentes con totales en cero para pintar siempre los 3 donuts. */
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

/**
 * Módulo Reportes — Dashboard analítico (HU #10247). Gráficos circulares por categoría
 * con filtro de fechas en tiempo real y acceso por rol. SuperAdmin puede seleccionar la
 * compañía a consultar; el Tenant Admin ve siempre la propia (claim `tenant_id`).
 */
export function Reportes() {
  const [range, setRange] = useState<DateRange>(() => defaultRange());
  const [isSuper, setIsSuper] = useState(false);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [tenantId, setTenantId] = useState("");

  const [data, setData] = useState<AnalyticsOverviewResponse | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [errorMessage, setErrorMessage] = useState<string>();
  const [reloadKey, setReloadKey] = useState(0);

  // Top 5 productividad (HU #10248, AC2) — carga independiente del overview.
  const [producers, setProducers] = useState<TopProducer[]>([]);
  const [producersStatus, setProducersStatus] = useState<UiStatus>("loading");
  const [producersError, setProducersError] = useState<string>();
  const [producersReloadKey, setProducersReloadKey] = useState(0);

  // Segmento seleccionado → panel lateral del detalle (HU #10248, AC1).
  const [segment, setSegment] = useState<SelectedSegment | null>(null);

  // Rol del usuario en cliente (el token solo existe tras el montaje).
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIsSuper(isSuperAdmin(decodeJwtPayload(getToken())));
  }, []);

  // Catálogo de compañías para el selector — solo SuperAdmin. Un fallo aquí no
  // bloquea el dashboard: el selector queda vacío y se usa la compañía propia.
  useEffect(() => {
    if (!isSuper) return;
    const controller = new AbortController();
    fetchCompaniesIndex({ pageSize: 100, estadoActivo: true }, controller.signal)
      .then((res) => {
        if (!controller.signal.aborted) setCompanies(res.data);
      })
      .catch(() => {
        /* silencioso: el dashboard sigue operativo sin el selector poblado */
      });
    return () => controller.abort();
  }, [isSuper]);

  // Carga del overview. Se redispara al cambiar el rango, la compañía o el reintento.
  useEffect(() => {
    const controller = new AbortController();

    async function load() {
      if (!isValidRange(range)) {
        setStatus("error");
        setErrorMessage("La fecha inicial no puede ser posterior a la fecha final.");
        return;
      }
      setStatus("loading");
      try {
        const res = await fetchAnalyticsOverview(
          { from: range.from, to: range.to, tenantId: tenantId || undefined },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setData(res);
        const hasData = res.categories.some((c) => c.total > 0);
        setStatus(hasData ? "ready" : "empty");
      } catch (error) {
        if (controller.signal.aborted || (error as Error).name === "AbortError") return;
        setErrorMessage(describeError(error));
        setStatus("error");
      }
    }

    void load();
    return () => controller.abort();
  }, [range, tenantId, reloadKey]);

  // Carga del Top 5 productividad. Se redispara igual que el overview.
  useEffect(() => {
    const controller = new AbortController();

    async function load() {
      if (!isValidRange(range)) {
        setProducersStatus("empty");
        return;
      }
      setProducersStatus("loading");
      try {
        const res = await fetchTopProducers(
          { from: range.from, to: range.to, limit: 5, tenantId: tenantId || undefined },
          controller.signal,
        );
        if (controller.signal.aborted) return;
        setProducers(res.items);
        setProducersStatus(res.items.length === 0 ? "empty" : "ready");
      } catch (error) {
        if (controller.signal.aborted || (error as Error).name === "AbortError") return;
        setProducersError(describeError(error));
        setProducersStatus("error");
      }
    }

    void load();
    return () => controller.abort();
  }, [range, tenantId, producersReloadKey]);

  const byCategory = useMemo(() => normalizeCategories(data), [data]);
  const totalTramites = useMemo(
    () => CATEGORY_ORDER.reduce((acc, c) => acc + byCategory[c].total, 0),
    [byCategory],
  );

  const retry = useCallback(() => setReloadKey((k) => k + 1), []);
  const retryProducers = useCallback(() => setProducersReloadKey((k) => k + 1), []);
  const selectSegment = useCallback(
    (category: AnalyticsCategory, segmentStatus?: string) => setSegment({ category, status: segmentStatus }),
    [],
  );
  const activeKey = segment ? `${segment.category}:${segment.status ?? ""}` : undefined;

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Reportes y Analíticas"
        subtitle="Monitorea el desempeño operativo por categoría de trámite."
      />

      {/* Filtros — rango de fechas (todos) + compañía (solo SuperAdmin) + exportaciones */}
      <div className="flex flex-wrap items-end gap-3 shrink-0">
        <DateRangeFilter value={range} onChange={setRange} disabled={status === "loading"} />
        {isSuper && (
          <CompanySelector
            companies={companies}
            value={tenantId}
            onChange={setTenantId}
            disabled={status === "loading"}
          />
        )}
        <div className="ml-auto">
          <ExportButtons
            range={range}
            tenantId={tenantId || undefined}
            category={segment?.category}
            status={segment?.status}
            disabled={status === "loading" || !isValidRange(range)}
          />
        </div>
      </div>

      <div className="pr-1">
        <UiStateBoundary
          status={status}
          errorMessage={errorMessage}
          onRetry={retry}
          emptyMessage="No hay trámites para el rango de fechas seleccionado."
          skeletonRows={3}
        >
          <div className="flex flex-col gap-4">
            {/* Resumen total + por categoría */}
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
              <SummaryCard label="Total trámites" value={totalTramites} color="#162744" />
              {CATEGORY_ORDER.map((cat) => (
                <SummaryCard
                  key={cat}
                  label={CATEGORY_META[cat].label}
                  value={byCategory[cat].total}
                  color={CATEGORY_META[cat].color}
                />
              ))}
            </div>

            {/* Gráficos circulares por categoría (segmentos clicables → detalle lateral) */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
              {CATEGORY_ORDER.map((cat) => (
                <CategoryDonut
                  key={cat}
                  metrics={byCategory[cat]}
                  onSelect={selectSegment}
                  activeKey={activeKey}
                />
              ))}
            </div>

            {/* Top 5 productividad con multiselect */}
            <ProductivityCards
              producers={producers}
              status={producersStatus}
              errorMessage={producersError}
              onRetry={retryProducers}
            />
          </div>
        </UiStateBoundary>
      </div>

      {segment && (
        <ProcedureDetailPanel
          key={activeKey}
          category={segment.category}
          status={segment.status}
          range={range}
          tenantId={tenantId || undefined}
          onClose={() => setSegment(null)}
        />
      )}
    </div>
  );
}

function SummaryCard({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div
      className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border flex flex-col justify-between min-h-[88px]"
    >
      <p className="text-[11px] opacity-70 font-medium">{label}</p>
      <p className="text-2xl font-bold mt-1" style={{ color }}>
        {value}
      </p>
    </div>
  );
}

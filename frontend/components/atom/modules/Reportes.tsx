"use client";

// Módulo Reportes 2.0 (HU-C): 5 pestañas temáticas con filtros globales
// persistentes, visibilidad por permiso RBAC (§3 del contrato) y drill-down
// compartido al detalle de trámites. El dashboard original (HU #10247/#10248)
// se recoloca en la pestaña "Resumen general" sin duplicarse.
import { useCallback, useEffect, useMemo, useState } from "react";
import { CalendarClock, ShieldQuestion } from "lucide-react";
import { usePermissions } from "@/hooks/usePermissions";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import type { AnalyticsCategory, CompanyListItem } from "@/lib/api/types";
import { ModuleTitle } from "./ModuleTitle";
import { ExportButtons } from "./_reportes/ExportButtons";
import { defaultFilters, type ReportFilters } from "./_reportes/filters";
import { GlobalFilters } from "./_reportes/GlobalFilters";
import { ProcedureDetailPanel } from "./_reportes/ProcedureDetailPanel";
import { isValidRange } from "./_reportes/range";
import { ReportesTabBar } from "./_reportes/ReportesTabBar";
import { SchedulingPanel } from "./_reportes/scheduling/SchedulingPanel";
import { OperacionTab } from "./_reportes/tabs/OperacionTab";
import { OrganismoTab } from "./_reportes/tabs/OrganismoTab";
import { ProductividadTab } from "./_reportes/tabs/ProductividadTab";
import { ResumenTab } from "./_reportes/tabs/ResumenTab";
import { UsoTab } from "./_reportes/tabs/UsoTab";
import { ConsultasTab } from "./_reportes/tabs/ConsultasTab";

type TabId = "resumen" | "operacion" | "ot" | "uso" | "productividad" | "consultas";

/** Pestañas + slug RBAC que las hace visibles (§3). SuperAdmin las ve todas. */
const TAB_DEFS: ReadonlyArray<{ id: TabId; label: string; slug: string }> = [
  { id: "resumen", label: "Resumen general", slug: "reportes.resumen.read" },
  { id: "operacion", label: "Operación / Trámites", slug: "reportes.operacion.read" },
  { id: "ot", label: "Organismo de Tránsito", slug: "reportes.ot.read" },
  { id: "uso", label: "Uso del aplicativo", slug: "reportes.uso.read" },
  { id: "productividad", label: "Productividad", slug: "reportes.productividad.read" },
  { id: "consultas", label: "Consultas personalizadas", slug: "reportes.consultas.read" },
];

/** Slug legado: hace visible al menos "Resumen general" (compatibilidad §3). */
const LEGACY_SLUG = "reportes.read";

/** Slug que habilita la administración de informes programados y alertas (HU-D). */
const SCHEDULING_SLUG = "reportes.programacion.manage";
const TAB_QUERY_PARAM = "reportesTab";

/** Pestañas con exportaciones (Excel/PDF ejecutivo con los filtros activos). */
const EXPORT_TABS: ReadonlyArray<TabId> = ["resumen", "operacion", "productividad"];

/** Segmento seleccionado en cualquier gráfica → detalle lateral (drill-down). */
interface SelectedSegment {
  category?: AnalyticsCategory;
  status?: string;
}

/**
 * La compañía elegida viaja en la dirección, junto a la pestaña y a la consulta.
 *
 * <p>Sin esto, un enlace copiado desde Consultas llega incompleto a quien lo abre: lleva los
 * filtros de la consulta pero no sobre qué compañía se preguntaba, así que un Super Admin lo abre
 * en «Todas las compañías» y ve el aviso de que falta elegir una — con la consulta cargada y sin un
 * solo dato. Al organismo no le pasa porque su identificador va en la ruta.</p>
 *
 * <p>Es el identificador interno del tenant, el mismo que ya viaja en las llamadas a la API; no
 * lleva nada de la persona que abre el enlace.</p>
 */
const COMPANY_QUERY_PARAM = "compania";

function initialTab(): string {
  if (typeof window === "undefined") return "";
  return new URLSearchParams(window.location.search).get(TAB_QUERY_PARAM) ?? "";
}

function initialFilters(): ReportFilters {
  const base = defaultFilters();
  if (typeof window === "undefined") return base;
  const tenantId = new URLSearchParams(window.location.search).get(COMPANY_QUERY_PARAM);
  return tenantId ? { ...base, tenantId } : base;
}

export function Reportes() {
  const { permissions, isSuperAdmin: isSuper } = usePermissions();

  const visibleTabs = useMemo(
    () =>
      TAB_DEFS.filter(
        (tab) =>
          isSuper ||
          permissions.includes(tab.slug) ||
          (tab.id === "resumen" && permissions.includes(LEGACY_SLUG)),
      ),
    [isSuper, permissions],
  );

  // Pestaña activa: persiste en el query param `reportesTab` sin recargar la página.
  const [requestedTab, setRequestedTab] = useState<string>(() => initialTab());
  const activeTab: TabId | undefined = visibleTabs.some((t) => t.id === requestedTab)
    ? (requestedTab as TabId)
    : visibleTabs[0]?.id;

  const selectTab = useCallback((id: string) => {
    setRequestedTab(id);
    try {
      const url = new URL(window.location.href);
      url.searchParams.set(TAB_QUERY_PARAM, id);
      window.history.replaceState(window.history.state, "", url);
    } catch {
      /* entorno sin history (tests/SSR): el estado local basta */
    }
  }, []);

  // Filtros globales persistentes: se conservan al cambiar de pestaña.
  const [filters, setFiltersState] = useState<ReportFilters>(() => initialFilters());

  // La compañía se refleja en la dirección con `replaceState`, igual que la pestaña: con
  // `pushState`, cambiar de compañía tres veces obligaría a tres «atrás» para salir del módulo.
  const setFilters = useCallback((next: ReportFilters) => {
    setFiltersState(next);
    try {
      const url = new URL(window.location.href);
      if (next.tenantId) url.searchParams.set(COMPANY_QUERY_PARAM, next.tenantId);
      else url.searchParams.delete(COMPANY_QUERY_PARAM);
      window.history.replaceState(window.history.state, "", url);
    } catch {
      /* entorno sin history (tests/SSR): el estado local basta */
    }
  }, []);
  const rangeValid = isValidRange(filters.range);

  // Los 4 endpoints nuevos EXIGEN tenantId para SuperAdmin (§4): sin compañía
  // elegida, las pestañas nuevas muestran el aviso en lugar de llamar a la API.
  const needsCompany = isSuper && !filters.tenantId;

  // Catálogo de compañías para el selector — solo SuperAdmin. Un fallo aquí no
  // bloquea el módulo: el selector queda vacío.
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  useEffect(() => {
    if (!isSuper) return;
    const controller = new AbortController();
    fetchCompaniesIndex({ pageSize: 100, estadoActivo: true }, controller.signal)
      .then((res) => {
        if (!controller.signal.aborted) setCompanies(res.data);
      })
      .catch(() => {
        /* silencioso */
      });
    return () => controller.abort();
  }, [isSuper]);

  // Programación y alertas (HU-D): visible con su permiso; SuperAdmin bypass.
  const canManageScheduling = isSuper || permissions.includes(SCHEDULING_SLUG);
  const [schedulingOpen, setSchedulingOpen] = useState(false);

  // Drill-down compartido: cualquier gráfica abre el panel lateral de detalle.
  const [segment, setSegment] = useState<SelectedSegment | null>(null);
  const openSegment = useCallback((next: SelectedSegment) => setSegment(next), []);
  const activeSegmentKey = segment ? `${segment.category ?? ""}:${segment.status ?? ""}` : undefined;

  // Sin ninguna pestaña visible → estado vacío amable (§3).
  if (visibleTabs.length === 0) {
    return (
      <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
        <ModuleTitle title="Reportes y Analíticas" subtitle="Monitorea el desempeño operativo por pestañas temáticas." />
        <div
          className="flex flex-col items-center justify-center gap-3 rounded-2xl border p-10 text-center bg-white dark:bg-[#0B0F14]"
          data-testid="reportes-sin-permisos"
        >
          <ShieldQuestion className="h-10 w-10 opacity-50" aria-hidden="true" />
          <p className="text-sm font-medium">No tienes permisos para ver reportes.</p>
          <p className="text-xs opacity-70 max-w-md">
            Pide a tu administrador que te asigne acceso a alguna pestaña de reportes
            (Resumen general, Operación, Organismo de Tránsito, Uso, Productividad o Consultas).
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Reportes y Analíticas"
        subtitle="Monitorea el desempeño operativo por pestañas temáticas."
      />

      <ReportesTabBar
        tabs={visibleTabs.map(({ id, label }) => ({ id, label }))}
        activeId={activeTab ?? ""}
        onChange={selectTab}
        ariaLabel="Pestañas de reportes"
      />

      {/* Filtros globales (persisten entre pestañas) + exportaciones. Van debajo de las pestañas:
          el usuario primero elige qué quiere ver y luego filtra esa vista. */}
      <div className="flex flex-wrap items-end gap-3 shrink-0">
        <GlobalFilters
          filters={filters}
          onChange={setFilters}
          isSuper={isSuper}
          companies={companies}
          onlyCompany={activeTab === "consultas"}
        />
        {canManageScheduling && (
          <button
            type="button"
            onClick={() => setSchedulingOpen(true)}
            className="inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-medium hover:bg-[#F4F7FC] dark:hover:bg-white/5"
            data-testid="reportes-abrir-programacion"
          >
            <CalendarClock className="h-4 w-4" aria-hidden="true" />
            Programación y alertas
          </button>
        )}
        {activeTab && EXPORT_TABS.includes(activeTab) && (
          <div className="ml-auto">
            <ExportButtons
              range={filters.range}
              tenantId={filters.tenantId || undefined}
              category={segment?.category}
              status={segment?.status}
              disabled={!rangeValid}
            />
          </div>
        )}
      </div>

      {!rangeValid && activeTab !== "consultas" ? (
        <div
          role="alert"
          className="flex flex-col items-center justify-center gap-2 rounded-2xl border p-8 text-center bg-white dark:bg-[#0B0F14]"
        >
          <p className="text-sm font-medium">La fecha inicial no puede ser posterior a la fecha final.</p>
          <p className="text-xs opacity-70">Corrige el rango de fechas para volver a consultar las métricas.</p>
        </div>
      ) : (
        <div className="pr-1">
          {activeTab === "resumen" && (
            <ResumenTab
              filters={filters}
              needsCompany={needsCompany}
              onDrillDown={openSegment}
              activeSegmentKey={activeSegmentKey}
            />
          )}
          {activeTab === "operacion" && (
            <OperacionTab filters={filters} needsCompany={needsCompany} onDrillDown={openSegment} />
          )}
          {activeTab === "ot" && <OrganismoTab filters={filters} needsCompany={needsCompany} />}
          {activeTab === "uso" && <UsoTab filters={filters} needsCompany={needsCompany} />}
          {activeTab === "productividad" && <ProductividadTab filters={filters} />}
          {activeTab === "consultas" && (
            <ConsultasTab
              tenantId={filters.tenantId || undefined}
              needsCompany={needsCompany}
              isSuper={isSuper}
            />
          )}
        </div>
      )}

      {canManageScheduling && (
        <SchedulingPanel
          open={schedulingOpen}
          onClose={() => setSchedulingOpen(false)}
          tenantId={filters.tenantId || undefined}
        />
      )}

      {segment && (
        <ProcedureDetailPanel
          key={activeSegmentKey}
          category={segment.category}
          status={segment.status}
          range={filters.range}
          tenantId={filters.tenantId || undefined}
          onClose={() => setSegment(null)}
        />
      )}
    </div>
  );
}

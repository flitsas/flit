"use client";

// Módulo Reportes V2 (HU #11114): 8 pestañas en orden canónico, ExportController
// global y navegación por teclado WCAG en ReportesTabBar.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Bookmark, Settings2, ShieldQuestion } from "lucide-react";
import { usePermissions } from "@/hooks/usePermissions";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import type { AnalyticsCategory, CompanyListItem } from "@/lib/api/types";
import { ModuleTitle } from "./ModuleTitle";
import { ExportButtons } from "./_reportes/ExportButtons";
import { ExportController } from "./_reportes/ExportController";
import { defaultFilters, type ReportFilters } from "./_reportes/filters";
import { GlobalFilters } from "./_reportes/GlobalFilters";
import { ProcedureDetailPanel } from "./_reportes/ProcedureDetailPanel";
import { isValidRange } from "./_reportes/range";
import { ReportesTabBar } from "./_reportes/ReportesTabBar";
import { ResumenTab } from "./_reportes/tabs/ResumenTab";
import { TramitesV2Tab } from "./_reportes/tabs/TramitesV2Tab";
import { ConsolidadoTab, ProductividadV2Tab } from "./_reportes/tabs/ConsolidadoProductividadTabs";
import { AuditoriaTab, SlaTab } from "./_reportes/tabs/SlaAuditoriaTabs";
import { ProgramadosTab, AlertasTab } from "./_reportes/tabs/ProgramadosAlertasTabs";
import { ReportFilterProvider, useReportFilters } from "./_reportes/ReportFilterContext";
import { DashboardPreferencesPanel } from "./_reportes/DashboardPreferencesPanel";
import { SavedQueriesPanel } from "./_reportes/SavedQueriesPanel";
import { getDashboardPreferences } from "@/lib/api/reporting-v2";
import {
  parseDashboardPreferences,
  type DashboardPreferencesConfig,
} from "./_reportes/dashboardPreferences";

/** Orden canónico HU #11114 AC1. */
export type ReportesV2TabId =
  | "resumen"
  | "tramites"
  | "consolidado"
  | "productividad"
  | "tiempos-sla"
  | "auditoria"
  | "programados"
  | "alertas";

export const REPORTES_V2_TAB_ORDER: ReadonlyArray<ReportesV2TabId> = [
  "resumen",
  "tramites",
  "consolidado",
  "productividad",
  "tiempos-sla",
  "auditoria",
  "programados",
  "alertas",
];

/** Pestañas + slug RBAC. SuperAdmin las ve todas. */
const TAB_DEFS: ReadonlyArray<{ id: ReportesV2TabId; label: string; slug: string }> = [
  { id: "resumen", label: "Resumen", slug: "reporting.read" },
  { id: "tramites", label: "Trámites", slug: "reporting.read" },
  { id: "consolidado", label: "Consolidado", slug: "reporting.consolidado" },
  { id: "productividad", label: "Productividad", slug: "reporting.productivity" },
  { id: "tiempos-sla", label: "Tiempos / SLA", slug: "reporting.read" },
  { id: "auditoria", label: "Auditoría", slug: "reporting.audit" },
  { id: "programados", label: "Programados", slug: "reporting.schedules.read" },
  { id: "alertas", label: "Alertas", slug: "reporting.alerts.read" },
];

/** Compatibilidad: slugs legados que habilitan tabs V2. */
const LEGACY_SLUG = "reportes.read";
const LEGACY_SCHEDULING = "reportes.programacion.manage";
const LEGACY_RESUMEN = "reportes.resumen.read";
const TAB_QUERY_PARAM = "reportesTab";

const EXPORT_REPORT_TYPE: Partial<Record<ReportesV2TabId, string>> = {
  resumen: "summary",
  tramites: "procedures",
  consolidado: "consolidado",
  productividad: "productivity",
  "tiempos-sla": "sla",
  auditoria: "audit",
};

interface SelectedSegment {
  category?: AnalyticsCategory;
  status?: string;
}

function initialTab(): string {
  if (typeof window === "undefined") return "resumen";
  return new URLSearchParams(window.location.search).get(TAB_QUERY_PARAM) ?? "resumen";
}

function canSeeTab(
  tab: (typeof TAB_DEFS)[number],
  permissions: string[],
  isSuper: boolean,
): boolean {
  if (isSuper) return true;
  if (permissions.includes(tab.slug)) return true;
  if (tab.id === "resumen" && (permissions.includes(LEGACY_SLUG) || permissions.includes(LEGACY_RESUMEN)))
    return true;
  if (
    ["tramites", "tiempos-sla"].includes(tab.id) &&
    permissions.includes(LEGACY_SLUG)
  )
    return true;
  if (
    ["programados", "alertas"].includes(tab.id) &&
    permissions.includes(LEGACY_SCHEDULING)
  )
    return true;
  return false;
}

export function Reportes() {
  return (
    <ReportFilterProvider>
      <ReportesInner />
    </ReportFilterProvider>
  );
}

/** Solo sincroniza tenantId: from/to V2 quedan en default 30 días (HU #11115 AC1). */
function SyncLegacyFiltersToV2({ tenantId }: { tenantId: string }) {
  const { patchFilters } = useReportFilters();
  useEffect(() => {
    patchFilters({ tenantId });
  }, [tenantId, patchFilters]);
  return null;
}

function ReportesInner() {
  const { permissions, isSuperAdmin: isSuper } = usePermissions();
  const { filters: v2Filters } = useReportFilters();

  const visibleTabs = useMemo(
    () => TAB_DEFS.filter((tab) => canSeeTab(tab, permissions, isSuper)),
    [isSuper, permissions],
  );

  const [requestedTab, setRequestedTab] = useState<string>(() => initialTab());
  const activeTab: ReportesV2TabId | undefined = visibleTabs.some((t) => t.id === requestedTab)
    ? (requestedTab as ReportesV2TabId)
    : visibleTabs[0]?.id ?? "resumen";

  const selectTab = useCallback((id: string) => {
    setRequestedTab(id);
    try {
      const url = new URL(window.location.href);
      url.searchParams.set(TAB_QUERY_PARAM, id);
      window.history.replaceState(window.history.state, "", url);
    } catch {
      /* tests/SSR */
    }
  }, []);

  const [filters, setFilters] = useState<ReportFilters>(() => defaultFilters());
  const rangeValid = isValidRange(filters.range);
  const needsCompany = isSuper && !filters.tenantId;

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

  const [prefsOpen, setPrefsOpen] = useState(false);
  const [savedOpen, setSavedOpen] = useState(false);
  const [dashPrefs, setDashPrefs] = useState<DashboardPreferencesConfig | null>(null);

  useEffect(() => {
    let cancelled = false;
    getDashboardPreferences(filters.tenantId || undefined)
      .then((res) => {
        if (!cancelled) setDashPrefs(parseDashboardPreferences(res.configJson));
      })
      .catch(() => {
        if (!cancelled) setDashPrefs(parseDashboardPreferences(null));
      });
    return () => {
      cancelled = true;
    };
  }, [filters.tenantId]);

  const [segment, setSegment] = useState<SelectedSegment | null>(null);
  const openSegment = useCallback((next: SelectedSegment) => setSegment(next), []);
  const activeSegmentKey = segment ? `${segment.category ?? ""}:${segment.status ?? ""}` : undefined;

  const exportReportType =
    (activeTab && EXPORT_REPORT_TYPE[activeTab]) || "procedures";

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
            Pide a tu administrador que te asigne acceso a alguna pestaña de reportes V2
            (Resumen, Trámites, Consolidado, Productividad, Tiempos/SLA, Auditoría, Programados o Alertas).
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <SyncLegacyFiltersToV2 tenantId={filters.tenantId} />
      <ModuleTitle
        title="Reportes y Analíticas"
        subtitle="Monitorea el desempeño operativo por pestañas temáticas."
      />

      <div className="flex flex-wrap items-end gap-3 shrink-0">
        <GlobalFilters filters={filters} onChange={setFilters} isSuper={isSuper} companies={companies} />
        <button
          type="button"
          onClick={() => setPrefsOpen(true)}
          className="inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-medium hover:bg-[#F4F7FC] dark:hover:bg-white/5"
          data-testid="reportes-abrir-preferencias"
        >
          <Settings2 className="h-4 w-4" aria-hidden="true" />
          Preferencias
        </button>
        <button
          type="button"
          onClick={() => setSavedOpen(true)}
          className="inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-medium hover:bg-[#F4F7FC] dark:hover:bg-white/5"
          data-testid="reportes-abrir-consultas"
        >
          <Bookmark className="h-4 w-4" aria-hidden="true" />
          Consultas
        </button>
        <div className="ml-auto flex flex-col items-end gap-2">
          {activeTab && ["resumen", "tramites", "consolidado"].includes(activeTab) && (
            <ExportButtons
              range={filters.range}
              tenantId={filters.tenantId || undefined}
              category={segment?.category}
              status={segment?.status}
              disabled={!rangeValid}
            />
          )}
          {/* ExportController visible desde cualquier tab (AC #11114) */}
          <ExportController
            reportType={exportReportType}
            from={filters.range.from}
            to={filters.range.to}
            tenantId={filters.tenantId || undefined}
            disabled={!rangeValid}
          />
        </div>
      </div>

      <ReportesTabBar
        tabs={visibleTabs.map(({ id, label }) => ({ id, label }))}
        activeId={activeTab ?? "resumen"}
        onChange={selectTab}
        ariaLabel="Pestañas de reportes"
      />

      {!rangeValid ? (
        <div
          role="alert"
          className="flex flex-col items-center justify-center gap-2 rounded-2xl border p-8 text-center bg-white dark:bg-[#0B0F14]"
        >
          <p className="text-sm font-medium">La fecha inicial no puede ser posterior a la fecha final.</p>
          <p className="text-xs opacity-70">Corrige el rango de fechas para volver a consultar las métricas.</p>
        </div>
      ) : (
        <div
          className="pr-1"
          role="tabpanel"
          id={`reportes-panel-${activeTab}`}
          aria-labelledby={`reportes-tab-${activeTab}`}
        >
          {activeTab === "resumen" && (
            <ResumenTab
              filters={filters}
              needsCompany={needsCompany}
              onDrillDown={openSegment}
              activeSegmentKey={activeSegmentKey}
              kpiPreferences={dashPrefs}
            />
          )}
          {activeTab === "tramites" && (
            <TramitesV2Tab tenantId={v2Filters.tenantId || undefined} />
          )}
          {activeTab === "consolidado" && (
            <ConsolidadoTab
              from={v2Filters.from}
              to={v2Filters.to}
              tenantId={v2Filters.tenantId || undefined}
            />
          )}
          {activeTab === "productividad" && (
            <ProductividadV2Tab
              from={v2Filters.from}
              to={v2Filters.to}
              tenantId={v2Filters.tenantId || undefined}
            />
          )}
          {activeTab === "tiempos-sla" && (
            <SlaTab
              from={v2Filters.from}
              to={v2Filters.to}
              tenantId={v2Filters.tenantId || undefined}
            />
          )}
          {activeTab === "auditoria" && (
            <AuditoriaTab
              from={v2Filters.from}
              to={v2Filters.to}
              tenantId={v2Filters.tenantId || undefined}
            />
          )}
          {activeTab === "programados" && (
            <ProgramadosTab tenantId={filters.tenantId || undefined} />
          )}
          {activeTab === "alertas" && (
            <AlertasTab tenantId={filters.tenantId || undefined} />
          )}
        </div>
      )}

      <DashboardPreferencesPanel
        open={prefsOpen}
        onClose={() => setPrefsOpen(false)}
        tenantId={filters.tenantId || undefined}
        onSaved={setDashPrefs}
      />
      <SavedQueriesPanel
        open={savedOpen}
        onClose={() => setSavedOpen(false)}
        tenantId={filters.tenantId || undefined}
      />

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

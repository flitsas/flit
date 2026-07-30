// Catálogo de KPIs configurables del dashboard Resumen (HU #11118 / G9).
export type DashboardKpiId =
  | "totalTramites"
  | "matriculas"
  | "traspasos"
  | "vehicular"
  | "otros"
  | "tramitesRechazados";

export interface DashboardKpiDef {
  id: DashboardKpiId;
  label: string;
}

export const DASHBOARD_KPI_DEFS: readonly DashboardKpiDef[] = [
  { id: "totalTramites", label: "Total trámites" },
  { id: "matriculas", label: "Matrículas" },
  { id: "traspasos", label: "Traspasos" },
  { id: "vehicular", label: "Vehicular" },
  { id: "otros", label: "Otros" },
  { id: "tramitesRechazados", label: "Trámites rechazados" },
] as const;

export interface DashboardKpiPreference {
  id: DashboardKpiId;
  visible: boolean;
}

export interface DashboardPreferencesConfig {
  kpis: DashboardKpiPreference[];
}

export const MAX_SAVED_QUERIES = 20;

export function defaultDashboardPreferences(): DashboardPreferencesConfig {
  return {
    kpis: DASHBOARD_KPI_DEFS.map((k) => ({ id: k.id, visible: true })),
  };
}

/** Normaliza config_json del backend a un orden/visibilidad completos. */
export function parseDashboardPreferences(raw: unknown): DashboardPreferencesConfig {
  const base = defaultDashboardPreferences();
  if (!raw || typeof raw !== "object") return base;
  const root = raw as Record<string, unknown>;
  const list = Array.isArray(root.kpis) ? root.kpis : null;
  if (!list) return base;

  const byId = new Map<DashboardKpiId, boolean>();
  const order: DashboardKpiId[] = [];
  for (const item of list) {
    if (!item || typeof item !== "object") continue;
    const row = item as Record<string, unknown>;
    const id = String(row.id ?? "") as DashboardKpiId;
    if (!DASHBOARD_KPI_DEFS.some((d) => d.id === id)) continue;
    if (!order.includes(id)) order.push(id);
    byId.set(id, row.visible !== false);
  }

  const kpis: DashboardKpiPreference[] = [];
  for (const id of order) {
    kpis.push({ id, visible: byId.get(id) ?? true });
  }
  for (const def of DASHBOARD_KPI_DEFS) {
    if (!kpis.some((k) => k.id === def.id)) {
      kpis.push({ id: def.id, visible: true });
    }
  }
  return { kpis };
}

export function moveKpi(
  kpis: DashboardKpiPreference[],
  index: number,
  direction: -1 | 1,
): DashboardKpiPreference[] {
  const next = [...kpis];
  const target = index + direction;
  if (target < 0 || target >= next.length) return kpis;
  const tmp = next[index]!;
  next[index] = next[target]!;
  next[target] = tmp;
  return next;
}

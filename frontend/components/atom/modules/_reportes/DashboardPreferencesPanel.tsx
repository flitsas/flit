"use client";

import { useCallback, useEffect, useId, useState } from "react";
import { Settings2, X } from "lucide-react";
import {
  getDashboardPreferences,
  putDashboardPreferences,
} from "@/lib/api/reporting-v2";
import {
  DASHBOARD_KPI_DEFS,
  moveKpi,
  parseDashboardPreferences,
  type DashboardKpiPreference,
  type DashboardPreferencesConfig,
} from "./dashboardPreferences";

export function DashboardPreferencesPanel({
  open,
  onClose,
  tenantId,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  tenantId?: string;
  onSaved?: (config: DashboardPreferencesConfig) => void;
}) {
  const titleId = useId();
  const liveId = useId();
  const [kpis, setKpis] = useState<DashboardKpiPreference[]>([]);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [announce, setAnnounce] = useState("");

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga preferencias al abrir panel
    setLoading(true);
    setError(null);
    getDashboardPreferences(tenantId)
      .then((res) => {
        if (cancelled) return;
        setKpis(parseDashboardPreferences(res.configJson).kpis);
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "No se pudieron cargar preferencias");
          setKpis(parseDashboardPreferences(null).kpis);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [open, tenantId]);

  const persist = useCallback(
    async (next: DashboardKpiPreference[]) => {
      setSaving(true);
      setError(null);
      try {
        const config: DashboardPreferencesConfig = { kpis: next };
        await putDashboardPreferences(config as unknown as Record<string, unknown>, tenantId);
        onSaved?.(config);
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : "No se pudo guardar");
      } finally {
        setSaving(false);
      }
    },
    [onSaved, tenantId],
  );

  const toggleVisible = async (id: string) => {
    const next = kpis.map((k) => (k.id === id ? { ...k, visible: !k.visible } : k));
    setKpis(next);
    await persist(next);
  };

  const reorder = async (index: number, direction: -1 | 1) => {
    const next = moveKpi(kpis, index, direction);
    if (next === kpis) return;
    setKpis(next);
    const moved = next[index + direction];
    const label = DASHBOARD_KPI_DEFS.find((d) => d.id === moved?.id)?.label ?? moved?.id;
    setAnnounce(`KPI ${label} en posición ${index + direction + 1} de ${next.length}`);
    setSelectedIndex(index + direction);
    await persist(next);
  };

  const onKeyDown = async (e: React.KeyboardEvent, index: number) => {
    if (e.key === " " || e.key === "Enter") {
      e.preventDefault();
      setSelectedIndex((prev) => (prev === index ? null : index));
      return;
    }
    if (selectedIndex !== index) return;
    if (e.key === "ArrowUp") {
      e.preventDefault();
      await reorder(index, -1);
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      await reorder(index, 1);
    }
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-40 flex justify-end bg-black/30"
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
    >
      <button type="button" className="flex-1 cursor-default" aria-label="Cerrar preferencias" onClick={onClose} />
      <aside className="h-full w-full max-w-md overflow-y-auto border-l bg-white p-4 shadow-xl dark:bg-[#0B0F14]">
        <div className="mb-4 flex items-center justify-between gap-2">
          <h2 id={titleId} className="text-sm font-bold inline-flex items-center gap-2">
            <Settings2 className="h-4 w-4" aria-hidden />
            Preferencias del dashboard
          </h2>
          <button type="button" onClick={onClose} className="rounded-lg border p-1.5" aria-label="Cerrar">
            <X className="h-4 w-4" />
          </button>
        </div>
        <p className="mb-3 text-xs opacity-70">
          Mostrar/ocultar KPIs. Space/Enter selecciona; flechas reordenan. Los cambios se guardan al confirmar.
        </p>
        <div id={liveId} className="sr-only" aria-live="polite">
          {announce}
        </div>
        {loading && <p className="text-sm opacity-70">Cargando…</p>}
        {error && (
          <p className="mb-2 text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
        {!loading && (
          <ul className="space-y-2" aria-label="Lista de KPIs">
            {kpis.map((kpi, index) => {
              const def = DASHBOARD_KPI_DEFS.find((d) => d.id === kpi.id);
              const selected = selectedIndex === index;
              return (
                <li
                  key={kpi.id}
                  className={`flex items-center justify-between gap-2 rounded-xl border px-3 py-2 ${
                    selected ? "ring-2 ring-[#162744]" : ""
                  }`}
                >
                  <button
                    type="button"
                    className="flex-1 text-left text-sm"
                    aria-pressed={selected}
                    aria-label={`${def?.label ?? kpi.id}. ${selected ? "Seleccionado para reordenar" : "Pulsa Space para seleccionar"}`}
                    onKeyDown={(e) => void onKeyDown(e, index)}
                    onClick={() => setSelectedIndex((prev) => (prev === index ? null : index))}
                  >
                    {def?.label ?? kpi.id}
                  </button>
                  <label className="inline-flex items-center gap-2 text-xs">
                    <span className="sr-only">Visible</span>
                    <input
                      type="checkbox"
                      checked={kpi.visible}
                      disabled={saving}
                      onChange={() => void toggleVisible(kpi.id)}
                      data-testid={`pref-kpi-${kpi.id}`}
                    />
                    Visible
                  </label>
                </li>
              );
            })}
          </ul>
        )}
        {saving && <p className="mt-2 text-xs opacity-60">Guardando…</p>}
      </aside>
    </div>
  );
}

"use client";

import { useEffect, useState } from "react";
import { BellRing, CalendarClock, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { SchedulesSection } from "./SchedulesSection";
import { AlertsSection } from "./AlertsSection";
import type { SchedulePresetConsulta } from "./ScheduleForm";

export interface SchedulingPanelProps {
  open: boolean;
  onClose: () => void;
  /** Solo SuperAdmin: compañía objetivo (los endpoints HU-D exigen tenant concreto). */
  tenantId?: string;
  /**
   * "Programar este informe" (HU-D, segunda ola): abre directo en "Informes programados" con el
   * formulario de creación ya prellenado. El alcance de la consulta (empresa/superadmin) decide
   * si la sección usa el CRUD de empresa o el de SuperAdmin — ver {@link SchedulesSection}.
   */
  presetConsulta?: SchedulePresetConsulta | null;
  /** Se llama una vez el preset ya abrió el formulario, para no reabrirlo en cada render. */
  onConsumePreset?: () => void;
}

type Tab = "schedules" | "alerts";

/**
 * Panel "Programación y alertas" — Reportes 2.0, HU-D. Modal autocontenido con dos
 * sub-pestañas: "Informes programados" (CRUD de report-schedules) y "Alertas" (CRUD de
 * alert-rules + historial de disparos). La integración lo monta desde Reportes con un
 * botón; este componente es el entregable standalone (no toca Reportes.tsx).
 */
export function SchedulingPanel({
  open, onClose, tenantId, presetConsulta, onConsumePreset,
}: SchedulingPanelProps) {
  const [tab, setTab] = useState<Tab>("schedules");

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reacciona a un preset que llega de fuera, no a estado propio
    if (presetConsulta) setTab("schedules");
  }, [presetConsulta]);

  if (!open) return null;

  const tabClass = (active: boolean) =>
    cn(
      "flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors",
      active
        ? "text-white"
        : "text-[#162744] dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800",
    );

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 p-4 sm:p-8"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Programación y alertas"
        data-testid="scheduling-panel"
        className="w-full max-w-4xl rounded-3xl bg-white dark:bg-slate-900 shadow-xl border border-slate-200 dark:border-slate-700"
      >
        <header className="flex items-center justify-between border-b border-slate-200 dark:border-slate-700 px-5 py-4">
          <div>
            <h2 className="text-lg font-bold" style={{ color: "#557EFF" }}>
              Programación y alertas
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Informes periódicos por correo y alertas por umbral sobre las métricas del tenant.
            </p>
          </div>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className="rounded-xl border border-slate-200 dark:border-slate-700 p-2 text-[#162744] dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </header>

        <nav className="flex items-center gap-2 px-5 pt-4" aria-label="Secciones de programación">
          <button
            type="button"
            onClick={() => setTab("schedules")}
            className={tabClass(tab === "schedules")}
            style={tab === "schedules" ? { background: "#557EFF" } : undefined}
            aria-current={tab === "schedules" ? "page" : undefined}
          >
            <CalendarClock className="h-4 w-4" aria-hidden="true" /> Informes programados
          </button>
          <button
            type="button"
            onClick={() => setTab("alerts")}
            className={tabClass(tab === "alerts")}
            style={tab === "alerts" ? { background: "#557EFF" } : undefined}
            aria-current={tab === "alerts" ? "page" : undefined}
          >
            <BellRing className="h-4 w-4" aria-hidden="true" /> Alertas
          </button>
        </nav>

        <div className="p-5">
          {tab === "schedules" ? (
            <SchedulesSection
              tenantId={tenantId}
              superAdminMode={presetConsulta?.savedQueryScope === "superadmin"}
              presetConsulta={presetConsulta ?? null}
              onConsumePreset={onConsumePreset}
            />
          ) : (
            <AlertsSection tenantId={tenantId} />
          )}
        </div>
      </div>
    </div>
  );
}

export default SchedulingPanel;

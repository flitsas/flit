"use client";

import { DIALOGO_CERRAR_CLS, DIALOGO_PANEL_CLS, DIALOGO_TITULO_CLS, DIALOGO_VELO_CLS, FranjaDialogo } from "@/components/admin/dialogo-propio";
import { useEffect, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { cn } from "@/lib/utils";
import type { ReportType } from "@/lib/api/analytics-scheduling";
import { SchedulesSection } from "./SchedulesSection";
import { AlertsSection } from "./AlertsSection";
import type { SchedulePresetConsulta } from "./ScheduleForm";

export interface SchedulingPanelProps {
  open: boolean;
  onClose: () => void;
  /** Solo SuperAdmin: compañía objetivo (los endpoints HU-D exigen tenant concreto). */
  tenantId?: string;
  /** SuperAdmin sin compañía elegida en el filtro superior — ver {@link SchedulesSection}. */
  needsCompany?: boolean;
  /**
   * "Programar este informe" (HU-D, segunda ola): abre directo en "Informes programados" con el
   * formulario de creación ya prellenado. El alcance de la consulta (empresa/superadmin) decide
   * si la sección usa el CRUD de empresa o el de SuperAdmin — ver {@link SchedulesSection}.
   */
  presetConsulta?: SchedulePresetConsulta | null;
  /** Se llama una vez el preset ya abrió el formulario, para no reabrirlo en cada render. */
  onConsumePreset?: () => void;
  /** Alcance Organismo de Tránsito (Reportes 2.0, HU-D, tercera ola): gestiona los informes y
   * alertas propios de ESE organismo — ver {@link SchedulesSection}/{@link AlertsSection}. */
  otTransitOfficeId?: string;
  /**
   * Restringe el selector "Tipo de informe" a estos (Reportes 2.0, HU-D, cuarta ola: el panel de
   * ICT solo ofrece sus 4 tipos ict_*). Ignorado cuando se pasa `otTransitOfficeId`, que ya trae
   * su propia restricción fija — ver {@link SchedulesSection}.
   */
  allowedReportTypes?: ReportType[];
}

type Tab = "schedules" | "alerts";

/**
 * Panel "Programación y alertas" — Reportes 2.0, HU-D. Modal autocontenido con dos
 * sub-pestañas: "Informes programados" (CRUD de report-schedules) y "Alertas" (CRUD de
 * alert-rules + historial de disparos). La integración lo monta desde Reportes con un
 * botón; este componente es el entregable standalone (no toca Reportes.tsx).
 */
export function SchedulingPanel({
  open, onClose, tenantId, needsCompany = false, presetConsulta, onConsumePreset, otTransitOfficeId,
  allowedReportTypes,
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
        ? "text-[color:var(--di-on-accent)]"
        : "text-flit-primary hover:bg-secondary",
    );

  return (
    <div
      className={`fixed inset-0 z-50 flex items-start justify-center overflow-y-auto p-4 sm:p-8 ${DIALOGO_VELO_CLS}`}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Programación y alertas"
        data-testid="scheduling-panel"
        className={`w-full max-w-4xl overflow-hidden ${DIALOGO_PANEL_CLS}`}
      >
        <header>
          <FranjaDialogo>
          <div>
            <h2 className={`text-lg font-bold ${DIALOGO_TITULO_CLS}`}>
              Programación y alertas
            </h2>
            <p className="text-xs opacity-85">
              {otTransitOfficeId
                ? "Informes periódicos por correo y alertas por umbral sobre el panel de tu organismo."
                : "Informes periódicos por correo y alertas por umbral sobre las métricas de tu compañía."}
            </p>
          </div>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className={DIALOGO_CERRAR_CLS}
          >
            <Icono nombre="cerrar" className="h-5 w-5" />
          </button>
          </FranjaDialogo>
        </header>

        <nav className="flex items-center gap-2 px-5 pt-4" aria-label="Secciones de programación">
          <button
            type="button"
            onClick={() => setTab("schedules")}
            className={tabClass(tab === "schedules")}
            style={tab === "schedules" ? { background: "var(--di-accent)" } : undefined}
            aria-current={tab === "schedules" ? "page" : undefined}
          >
            <Icono nombre="calendario" className="h-4 w-4" /> Informes programados
          </button>
          <button
            type="button"
            onClick={() => setTab("alerts")}
            className={tabClass(tab === "alerts")}
            style={tab === "alerts" ? { background: "var(--di-accent)" } : undefined}
            aria-current={tab === "alerts" ? "page" : undefined}
          >
            <Icono nombre="campana" className="h-4 w-4" /> Alertas
          </button>
        </nav>

        <div className="p-5">
          {tab === "schedules" ? (
            <SchedulesSection
              tenantId={tenantId}
              needsCompany={needsCompany}
              presetConsulta={presetConsulta ?? null}
              onConsumePreset={onConsumePreset}
              otTransitOfficeId={otTransitOfficeId}
              allowedReportTypes={allowedReportTypes}
            />
          ) : (
            <AlertsSection
              tenantId={tenantId}
              needsCompany={needsCompany}
              otTransitOfficeId={otTransitOfficeId}
            />
          )}
        </div>
      </div>
    </div>
  );
}

export default SchedulingPanel;

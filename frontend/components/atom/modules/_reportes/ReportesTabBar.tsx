"use client";

// Barra de pestañas del módulo Reportes (Reportes 2.0, HU-C).
// Copia local del patrón `components/admin/transit-offices/OtTabBar.tsx`,
// con soporte de dark mode en el color del texto inactivo.
import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";

export interface ReportesTabItem {
  id: string;
  label: string;
}

export interface ReportesTabBarProps {
  tabs: ReportesTabItem[];
  activeId: string;
  onChange: (id: string) => void;
  ariaLabel: string;
}

export function ReportesTabBar({ tabs, activeId, onChange, ariaLabel }: ReportesTabBarProps) {
  return (
    <div
      className={PESTANAS_LISTA_CLS}
      role="tablist"
      aria-label={ariaLabel}
    >
      {tabs.map((tab) => {
        const active = tab.id === activeId;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(tab.id)}
            className={pestanaClass(active)}
          >
            {tab.label}
            {active && (
              <span
                className="hidden"
              />
            )}
          </button>
        );
      })}
    </div>
  );
}

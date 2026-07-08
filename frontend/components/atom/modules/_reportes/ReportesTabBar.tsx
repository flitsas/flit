"use client";

// Barra de pestañas del módulo Reportes (Reportes 2.0, HU-C).
// Copia local del patrón `components/admin/transit-offices/OtTabBar.tsx`,
// con soporte de dark mode en el color del texto inactivo.
import { cn } from "@/lib/utils";

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
      className="flex items-center gap-1 overflow-x-auto border-b"
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
            className={cn(
              "relative shrink-0 px-4 py-2.5 text-xs font-semibold transition",
              active ? "text-[#557EFF] opacity-100" : "text-[#162744] dark:text-white opacity-65",
            )}
          >
            {tab.label}
            {active && (
              <span
                className="absolute inset-x-0 bottom-0 h-0.5 rounded-full"
                style={{ background: "#557EFF" }}
              />
            )}
          </button>
        );
      })}
    </div>
  );
}

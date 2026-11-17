"use client";

import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";

/** Barra de pestañas — patrón CompanyConfigTabs / DocumentProcedureTabs. */
export interface OtTabItem {
  id: string;
  label: string;
}

export interface OtTabBarProps {
  tabs: OtTabItem[];
  activeId: string;
  onChange: (id: string) => void;
  ariaLabel: string;
}

export function OtTabBar({ tabs, activeId, onChange, ariaLabel }: OtTabBarProps) {
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
            className={pestanaClass(active, "shrink-0")}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

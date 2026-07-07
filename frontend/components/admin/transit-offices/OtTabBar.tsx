"use client";

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
            className="relative shrink-0 px-4 py-2.5 text-xs font-semibold transition"
            style={{ color: active ? "#557EFF" : "#162744", opacity: active ? 1 : 0.65 }}
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

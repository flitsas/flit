"use client";

// Barra de pestañas Reportes V2 (HU #11114) — tablist WCAG con flechas.
import { useCallback, useRef, type KeyboardEvent } from "react";
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
  const refs = useRef<Array<HTMLButtonElement | null>>([]);

  const focusAt = useCallback(
    (index: number) => {
      const next = ((index % tabs.length) + tabs.length) % tabs.length;
      onChange(tabs[next]!.id);
      refs.current[next]?.focus();
    },
    [onChange, tabs],
  );

  const onKeyDown = useCallback(
    (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
      if (event.key === "ArrowRight" || event.key === "ArrowDown") {
        event.preventDefault();
        focusAt(index + 1);
      } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
        event.preventDefault();
        focusAt(index - 1);
      } else if (event.key === "Home") {
        event.preventDefault();
        focusAt(0);
      } else if (event.key === "End") {
        event.preventDefault();
        focusAt(tabs.length - 1);
      }
    },
    [focusAt, tabs.length],
  );

  return (
    <div
      className="flex items-center gap-1 overflow-x-auto border-b"
      role="tablist"
      aria-label={ariaLabel}
      data-testid="reportes-tablist"
    >
      {tabs.map((tab, index) => {
        const active = tab.id === activeId;
        return (
          <button
            key={tab.id}
            ref={(el) => {
              refs.current[index] = el;
            }}
            type="button"
            role="tab"
            id={`reportes-tab-${tab.id}`}
            aria-selected={active}
            aria-controls={`reportes-panel-${tab.id}`}
            tabIndex={active ? 0 : -1}
            data-testid={`reportes-tab-${tab.id}`}
            onClick={() => onChange(tab.id)}
            onKeyDown={(e) => onKeyDown(e, index)}
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

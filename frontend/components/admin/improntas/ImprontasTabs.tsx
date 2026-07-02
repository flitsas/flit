"use client";

import { useRouter } from "next/navigation";
import { IMPRONTAS_TABS, improntasTabPath, type ImprontasTabId } from "./improntas-nav";

/**
 * Barra de pestañas del módulo Improntas (HU #10470) — alterna entre el formulario de
 * generación y el historial. Mismo look & feel que `OtTabBar` (transit-offices), pero
 * copia local: el módulo Improntas no depende de `transit-offices` (cada feature admin
 * mantiene su propia barra de pestañas, patrón ya usado por `CompanyConfigTabs`).
 */
export function ImprontasTabs({ activeId }: { activeId: ImprontasTabId }) {
  const router = useRouter();

  return (
    <div
      className="flex items-center gap-1 overflow-x-auto border-b"
      style={{ borderColor: "#DFE5ED" }}
      role="tablist"
      aria-label="Secciones del módulo de improntas"
    >
      {IMPRONTAS_TABS.map((tab) => {
        const active = tab.id === activeId;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => router.push(improntasTabPath(tab.id))}
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

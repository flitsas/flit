"use client";

import { useRouter } from "next/navigation";
import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";
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
      className={PESTANAS_LISTA_CLS}
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
            className={pestanaClass(active, "relative shrink-0")}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

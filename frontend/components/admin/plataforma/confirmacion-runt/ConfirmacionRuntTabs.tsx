"use client";

import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";
import { useRouter } from "next/navigation";
import {
  confirmacionRuntTabPath,
  type ConfirmacionRuntTab,
  type ConfirmacionRuntTabId,
} from "./confirmacion-runt-nav";

/**
 * Barra de pestañas de Confirmación RUNT (HU #12313). Recibe SOLO las pestañas que el usuario puede
 * ver (`visibleConfirmacionRuntTabs`), así que con un único permiso pinta una sola pestaña. Mismo
 * look & feel que `ImprontasTabs`.
 */
export function ConfirmacionRuntTabs({
  tabs,
  activeId,
}: {
  tabs: ConfirmacionRuntTab[];
  activeId: ConfirmacionRuntTabId;
}) {
  const router = useRouter();

  return (
    <div
      className={PESTANAS_LISTA_CLS}
      role="tablist"
      aria-label="Secciones de Confirmación RUNT"
    >
      {tabs.map((tab) => {
        const active = tab.id === activeId;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => router.push(confirmacionRuntTabPath(tab.id))}
            className={pestanaClass(active, "shrink-0")}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

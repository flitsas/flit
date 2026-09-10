"use client";

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
      className="flex items-center gap-1 overflow-x-auto border-b border-[#DFE5ED] dark:border-white/10"
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

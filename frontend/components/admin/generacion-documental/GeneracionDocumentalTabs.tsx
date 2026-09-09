"use client";

import { useRouter } from "next/navigation";
import {
  GENERACION_DOCUMENTAL_TABS,
  generacionDocumentalTabPath,
  type GeneracionDocumentalTabId,
} from "./generacion-documental-nav";

/**
 * Barra de pestañas del módulo "Generación documental" (HU-01, CF-01): Certificado RUES,
 * Transferencia e Historial. Mismo look & feel y misma copia local que `ImprontasTabs`
 * (cada feature admin mantiene su propia barra). La navegación es del App Router, así que
 * cada pestaña monta su panel sin recargar la página.
 */
export function GeneracionDocumentalTabs({ activeId }: { activeId: GeneracionDocumentalTabId }) {
  const router = useRouter();

  return (
    <div
      className="flex items-center gap-1 overflow-x-auto border-b"
      role="tablist"
      aria-label="Secciones del módulo de generación documental"
    >
      {GENERACION_DOCUMENTAL_TABS.map((tab) => {
        const active = tab.id === activeId;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => router.push(generacionDocumentalTabPath(tab.id))}
            className="relative shrink-0 px-4 py-2.5 text-xs font-semibold transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            style={{ color: active ? "#557EFF" : "#162744", opacity: active ? 1 : 0.65 }}
          >
            {tab.label}
            {active && (
              <span
                className="absolute inset-x-0 bottom-0 h-0.5 rounded-full"
                style={{ background: "#557EFF" }}
                aria-hidden="true"
              />
            )}
          </button>
        );
      })}
    </div>
  );
}

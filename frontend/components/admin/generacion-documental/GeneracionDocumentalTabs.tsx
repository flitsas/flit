"use client";

import { useRouter } from "next/navigation";
import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";
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
      className={PESTANAS_LISTA_CLS}
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
            className={pestanaClass(active, "relative shrink-0")}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

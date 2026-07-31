"use client";

import { useMemo, useRef } from "react";
import { FileSignature, FileStack, Users } from "lucide-react";
import { CompanyTabsNavContext, type CompanyTabsNav } from "../CompanyConfigTabs";
import { SignatureVaultTab } from "../signature-vault/SignatureVaultTab";
import { LegalRepresentativesTab } from "./LegalRepresentativesTab";
import { CompanyDeedsSection } from "./CompanyDeedsSection";

/**
 * Pestaña "Representantes legales" (ajustes HU #10929). Une en una sola pestaña tres secciones
 * claramente diferenciadas:
 *  - "Representantes legales": el directorio de representantes (con su detalle representante-céntrico).
 *  - "Escrituras por compañía" (HU #11063): panorama de vigencia y carga/reemplazo en un paso, sin
 *    entrar al detalle de un representante. El alta desde el detalle sigue existiendo: responde a
 *    "qué escrituras asoció ESTE representante", que es otra pregunta.
 *  - "Baúl de firmas": las firmas de apoderados, visible solo si `baulFirmasActivo` está activo.
 *
 * El puente "Registrar en baúl" (`goToBaul`) ya no cambia de pestaña: hace scroll a la sección del
 * Baúl dentro de esta misma pestaña. Este componente es el proveedor del contexto de navegación que
 * la sección de representantes consume.
 */
export function RepresentativesAndVaultTab({
  tenantId,
  baulVisible,
}: {
  tenantId: string;
  baulVisible: boolean;
}) {
  const vaultRef = useRef<HTMLElement>(null);

  const nav = useMemo<CompanyTabsNav>(
    () => ({
      // Lleva a la sección del Baúl dentro de la misma pestaña (scroll), en vez de cambiar de pestaña.
      goToBaul: () => vaultRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }),
      baulVisible,
    }),
    [baulVisible],
  );

  return (
    <CompanyTabsNavContext.Provider value={nav}>
      <div className="space-y-8">
        <section aria-labelledby="representantes-heading">
          <h2
            id="representantes-heading"
            className="mb-3 flex items-center gap-2 text-sm font-bold"
            style={{ color: "#162744" }}
          >
            <Users className="h-4 w-4" style={{ color: "#557EFF" }} />
            Representantes legales
          </h2>
          <LegalRepresentativesTab tenantId={tenantId} />
        </section>

        {/* HU #11063 — escrituras como sección hermana: el recorrido para cargarlas ya no pasa por el
            detalle de un representante. */}
        <section
          aria-labelledby="escrituras-heading"
          className="border-t border-[#DFE5ED] pt-6 dark:border-white/10"
        >
          <h2
            id="escrituras-heading"
            className="mb-3 flex items-center gap-2 text-sm font-bold"
            style={{ color: "#162744" }}
          >
            <FileStack className="h-4 w-4" style={{ color: "#557EFF" }} />
            Escrituras por compañía
          </h2>
          <CompanyDeedsSection tenantId={tenantId} />
        </section>

        {baulVisible && (
          <section
            ref={vaultRef}
            aria-labelledby="baul-heading"
            className="border-t pt-6"
            style={{ borderColor: "#DFE5ED" }}
          >
            <h2
              id="baul-heading"
              className="mb-3 flex items-center gap-2 text-sm font-bold"
              style={{ color: "#162744" }}
            >
              <FileSignature className="h-4 w-4" style={{ color: "#557EFF" }} />
              Baúl de firmas
            </h2>
            <SignatureVaultTab tenantId={tenantId} />
          </section>
        )}
      </div>
    </CompanyTabsNavContext.Provider>
  );
}

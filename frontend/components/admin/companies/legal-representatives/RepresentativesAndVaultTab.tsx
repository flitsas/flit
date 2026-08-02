"use client";

import { useMemo, useRef } from "react";
import { FileSignature, Users } from "lucide-react";
import { CompanyTabsNavContext, type CompanyTabsNav } from "../CompanyConfigTabs";
import { SignatureVaultTab } from "../signature-vault/SignatureVaultTab";
import { LegalRepresentativesTab } from "./LegalRepresentativesTab";

/**
 * Pestaña "Representantes legales". Une en una sola pestaña dos secciones:
 *  - "Representantes legales": el directorio con el acordeón de compañías y sus escrituras (HU #11179).
 *  - "Baúl de firmas": las firmas de apoderados, visible solo si `baulFirmasActivo` está activo.
 *
 * HU #11179 (D4): la sección "Escrituras por compañía" (HU #11063) se retira. El único punto de
 * gestión de escrituras pasa a ser el acordeón dentro del detalle de cada representante. Esta es
 * una decisión de PO que revierte HU #11063 — no es un descuido técnico.
 *
 * El puente "Registrar en baúl" (`goToBaul`) hace scroll a la sección del Baúl dentro de esta misma
 * pestaña. Este componente es el proveedor del contexto de navegación que la sección de representantes
 * consume.
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

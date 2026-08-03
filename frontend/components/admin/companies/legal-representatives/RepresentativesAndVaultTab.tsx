"use client";

import { Users } from "lucide-react";
import { LegalRepresentativesTab } from "./LegalRepresentativesTab";
import { RL_COLOR } from "./rl-flit-styles";

/**
 * Pestaña "Representantes legales" (Admin compañías).
 *
 * Solo muestra el directorio de representantes. La firma del baúl y la identidad
 * se gestionan dentro de la ficha de cada persona (panel view/create/edit), no
 * como sección hermana en esta pantalla. Las escrituras viven bajo cada NIT
 * dentro del acordeón del representante.
 *
 * El componente conserva el nombre histórico `RepresentativesAndVaultTab` para
 * no romper imports; el baúl suelto ya no forma parte de esta vista.
 */
export function RepresentativesAndVaultTab({
  tenantId,
}: {
  tenantId: string;
  /** @deprecated Ya no se usa: el baúl no se muestra en esta pantalla. */
  baulVisible?: boolean;
}) {
  return (
    <div className="space-y-6">
      <section
        aria-labelledby="representantes-heading"
        className="rounded-2xl border bg-white p-4 shadow-sm sm:p-5"
        style={{ borderColor: RL_COLOR.border, boxShadow: "0 8px 24px rgba(22, 39, 68, 0.06)" }}
      >
        <header className="mb-4">
          <h2
            id="representantes-heading"
            className="flex items-center gap-2 text-sm font-bold"
            style={{ color: RL_COLOR.navy }}
          >
            <Users className="h-4 w-4" style={{ color: RL_COLOR.brand }} aria-hidden="true" />
            Representantes legales
          </h2>
        </header>
        <LegalRepresentativesTab tenantId={tenantId} />
      </section>
    </div>
  );
}

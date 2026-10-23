"use client";

import { LegalRepresentativesTab } from "./LegalRepresentativesTab";

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
  networkHeadId,
}: {
  tenantId: string;
  networkHeadId?: string | null;
  /** @deprecated Ya no se usa: el baúl no se muestra en esta pantalla. */
  baulVisible?: boolean;
}) {
  // Bug #13055 — sin tarjeta blanca envolvente: la tabla va directa, como en Trámites y el resto de
  // módulos homologados (cada fila ya es su propia tarjeta). La pestaña ya rotula la sección, así que
  // el título queda solo para lectores de pantalla.
  return (
    <section aria-labelledby="representantes-heading">
      <h2 id="representantes-heading" className="sr-only">
        Representantes legales
      </h2>
      <LegalRepresentativesTab tenantId={tenantId} networkHeadId={networkHeadId} />
    </section>
  );
}

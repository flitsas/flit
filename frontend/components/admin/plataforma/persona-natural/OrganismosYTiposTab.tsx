"use client";

// HU #14075 AC1 — Pestaña «Organismos y trámites»: qué trámites ofrece el portal de Persona Natural y en qué
// organismos de tránsito. Sin pestaña de datos bancarios: están fuera de alcance (decisión del PO).
import { InlineAlert } from "@/components/atom/InlineAlert";
import { TiposSection } from "./TiposSection";
import { OrganismosSection } from "./OrganismosSection";

export function OrganismosYTiposTab({ onIrATarifas, onCambio }: { onIrATarifas: () => void; onCambio?: () => void }) {
  return (
    <div className="flex flex-col gap-8">
      <InlineAlert tone="info" title="Cómo se decide qué ve el ciudadano">
        Un trámite aparece en el portal cuando está activado, el organismo está habilitado y existe una tarifa vigente
        para esa combinación. Si falta algo, el ciudadano no lo verá.
      </InlineAlert>
      <TiposSection onIrATarifas={onIrATarifas} onCambio={onCambio} />
      <OrganismosSection onIrATarifas={onIrATarifas} onCambio={onCambio} />
    </div>
  );
}

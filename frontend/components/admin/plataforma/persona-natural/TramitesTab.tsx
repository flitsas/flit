"use client";

// HU #14075 AC1 — Pestaña «Trámites» (paso 1): qué trámites ofrece el portal de Persona Natural.
import { InlineAlert } from "@/components/atom/InlineAlert";
import { TiposSection } from "./TiposSection";

export function TramitesTab({ onIrATarifas, onCambio }: { onIrATarifas: () => void; onCambio?: () => void }) {
  return (
    <div className="flex flex-col gap-6">
      <InlineAlert tone="info" title="Cómo se decide qué ve el ciudadano">
        Un trámite aparece en el portal cuando está activado, el organismo está habilitado y existe una tarifa vigente
        para esa combinación. Si falta algo, el ciudadano no lo verá.
      </InlineAlert>
      <TiposSection onIrATarifas={onIrATarifas} onCambio={onCambio} />
    </div>
  );
}

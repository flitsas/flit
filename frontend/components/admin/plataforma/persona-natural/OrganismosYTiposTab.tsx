"use client";

// HU #14075 AC1 — Pestaña «Organismos y tipos»: los tipos de trámite elegibles y los organismos de tránsito que el
// portal de Persona Natural puede ofrecer. Sin pestaña de datos bancarios: están fuera de alcance (decisión del PO).
import { TiposSection } from "./TiposSection";
import { OrganismosSection } from "./OrganismosSection";
import { PN_INFO_NOTICE_CLS } from "./pn-ui";

export function OrganismosYTiposTab({ onIrATarifas }: { onIrATarifas: () => void }) {
  return (
    <div className="flex flex-col gap-8">
      <p className={PN_INFO_NOTICE_CLS}>
        Regla: un trámite aparece en el portal cuando el tipo es elegible y está habilitado, el organismo está habilitado
        y hay una tarifa vigente.
      </p>
      <TiposSection onIrATarifas={onIrATarifas} />
      <OrganismosSection onIrATarifas={onIrATarifas} />
    </div>
  );
}

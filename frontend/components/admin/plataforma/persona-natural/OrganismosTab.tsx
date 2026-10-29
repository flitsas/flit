"use client";

// HU #14075 AC1 — Pestaña «Organismos» (paso 2): en qué organismos de tránsito puede iniciar trámites el ciudadano.
import { OrganismosSection } from "./OrganismosSection";

export function OrganismosTab({ onIrATarifas, onCambio }: { onIrATarifas: () => void; onCambio?: () => void }) {
  return <OrganismosSection onIrATarifas={onIrATarifas} onCambio={onCambio} />;
}

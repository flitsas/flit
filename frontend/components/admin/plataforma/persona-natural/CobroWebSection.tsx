"use client";

// HU #14394 (Feature #14392) — medios de pago de la web de persona en Plataforma, encima de las tarifas. Es la misma
// sección del hub del OT con el cliente de Plataforma y sin PIN (la web no lo ofrece).
import { OtCobroMediosSection } from "@/components/admin/transit-offices/portal-pn/OtCobroMediosSection";
import { crearCobroApiPlataforma } from "@/lib/api/admin-ot-portal-cobro";

const API_COBRO_WEB = crearCobroApiPlataforma();

export function CobroWebSection({ onIrAPrecios }: { onIrAPrecios: () => void }) {
  return (
    <OtCobroMediosSection
      api={API_COBRO_WEB}
      conPin={false}
      onIrAPrecios={onIrAPrecios}
      nombrePrecios="Tarifas"
    />
  );
}

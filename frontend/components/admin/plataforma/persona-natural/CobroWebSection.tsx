"use client";

// HU #14394 (Feature #14392) — cobro de la web de persona en Plataforma, en dos pasos (Tarifas → Medios de pago). Es la
// misma sección del hub del OT con el cliente de Plataforma y sin PIN (la web no lo ofrece).
import { OtCobroMediosSection } from "@/components/admin/transit-offices/portal-pn/OtCobroMediosSection";
import { crearCobroApiPlataforma } from "@/lib/api/admin-ot-portal-cobro";
import { CobroEnPasos } from "./CobroEnPasos";
import { TarifasTab } from "./TarifasTab";

const API_COBRO_WEB = crearCobroApiPlataforma();

export function CobroWebSection({ onCambio }: { onCambio?: () => void }) {
  return (
    <CobroEnPasos
      nombrePrecios="Tarifas"
      onCambio={onCambio}
      precios={(p) => <TarifasTab {...p} />}
      medios={(m) => <OtCobroMediosSection api={API_COBRO_WEB} conPin={false} nombrePrecios="Tarifas" {...m} />}
    />
  );
}

"use client";

// HU #14455 / #14462 (Feature #14453) — pestaña «Caja» del portal del OT: «Usuarios de caja» y «Cobros de caja».
import { useState } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { OtCajaCobrosSection } from "./OtCajaCobrosSection";
import { OtCajaUsuariosSection } from "./OtCajaUsuariosSection";

type CajaTabId = "usuarios" | "cobros";

export function OtCajaTab({ transitOfficeId }: { transitOfficeId: string }) {
  const [active, setActive] = useState<CajaTabId>("usuarios");
  const tabs: SectionTab<CajaTabId>[] = [
    { id: "usuarios", label: "Usuarios de caja", content: <OtCajaUsuariosSection transitOfficeId={transitOfficeId} /> },
    { id: "cobros", label: "Cobros de caja", content: <OtCajaCobrosSection transitOfficeId={transitOfficeId} /> },
  ];
  return <SectionTabs tabs={tabs} active={active} onChange={setActive} ariaLabel="Secciones de la caja del portal" />;
}

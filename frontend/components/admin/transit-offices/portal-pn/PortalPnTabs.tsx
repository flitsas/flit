"use client";

import { useState, type ReactNode } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { PortalPnDesignSection } from "./PortalPnDesignSection";

export type PortalPnTabId = "cobro" | "diseno";

export interface PortalPnTabsProps {
  transitOfficeId: string;
  /** Contenido de la subpestaña Cobro (precios del portal). Lo conecta la HU de precios; sin él, un aviso. */
  cobro?: ReactNode;
  /** Sección de dominio de Diseño (llega con otra HU). */
  domain?: ReactNode;
  /** Subpestaña inicial. Por defecto Cobro si ya tiene contenido; si no, Diseño. */
  initialTab?: PortalPnTabId;
}

/**
 * Subpestañas del Portal Persona Natural del organismo (Épica #14297): `Cobro` y `Diseño`. `Reportes` llega con su HU.
 * Es el mismo componente para el administrador del organismo y para el SuperAdmin: todo se ancla a `transitOfficeId`.
 */
export function PortalPnTabs({ transitOfficeId, cobro, domain, initialTab }: PortalPnTabsProps) {
  const [active, setActive] = useState<PortalPnTabId>(initialTab ?? (cobro ? "cobro" : "diseno"));

  const tabs: SectionTab<PortalPnTabId>[] = [
    {
      id: "cobro",
      label: "Cobro",
      content: cobro ?? (
        <p role="status" className="rounded-xl border px-4 py-6 text-center text-xs opacity-70" style={{ borderColor: "#DFE5ED" }}>
          Los precios de cobro del portal se configuran en esta pestaña.
        </p>
      ),
    },
    {
      id: "diseno",
      label: "Diseño",
      content: <PortalPnDesignSection transitOfficeId={transitOfficeId} domain={domain} />,
    },
  ];

  return <SectionTabs tabs={tabs} active={active} onChange={setActive} ariaLabel="Secciones del portal Persona Natural" />;
}

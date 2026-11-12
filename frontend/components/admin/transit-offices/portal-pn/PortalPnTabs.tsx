"use client";

import { useState, type ReactNode } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { PortalPnDesignSection } from "./PortalPnDesignSection";

export type PortalPnTabId = "reportes" | "cobro" | "diseno";

export interface PortalPnTabsProps {
  transitOfficeId: string;
  /** Contenido de la subpestaña Reportes (HU #14399). Sin él, un aviso. */
  reportes?: ReactNode;
  /** Contenido de la subpestaña Cobro (precios del portal). Lo conecta la HU de precios; sin él, un aviso. */
  cobro?: ReactNode;
  /** Sección de dominio de Diseño (llega con otra HU). */
  domain?: ReactNode;
  /** Subpestaña inicial. Por defecto Reportes, la primera. */
  initialTab?: PortalPnTabId;
}

/**
 * Subpestañas del Portal Persona Natural del organismo (Épica #14297): `Reportes` (la primera), `Cobro` y `Diseño`.
 * Es el mismo componente para el administrador del organismo y para el SuperAdmin: todo se ancla a `transitOfficeId`.
 */
export function PortalPnTabs({ transitOfficeId, reportes, cobro, domain, initialTab }: PortalPnTabsProps) {
  const [active, setActive] = useState<PortalPnTabId>(initialTab ?? "reportes");

  const tabs: SectionTab<PortalPnTabId>[] = [
    {
      id: "reportes",
      label: "Reportes",
      content: reportes ?? (
        <p role="status" className="rounded-xl border px-4 py-6 text-center text-xs opacity-70" style={{ borderColor: "var(--border)" }}>
          Los trámites que llegan por el portal se consultan en esta pestaña.
        </p>
      ),
    },
    {
      id: "cobro",
      label: "Cobro",
      content: cobro ?? (
        <p role="status" className="rounded-xl border px-4 py-6 text-center text-xs opacity-70" style={{ borderColor: "var(--border)" }}>
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

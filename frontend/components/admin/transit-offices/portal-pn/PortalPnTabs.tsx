"use client";

import { useState, type ReactNode } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { PortalPnDesignSection } from "./PortalPnDesignSection";

export type PortalPnTabId = "reportes" | "cobro" | "caja" | "diseno";

export interface PortalPnTabsProps {
  transitOfficeId: string;
  /** Contenido de la subpestaña Reportes (HU #14399). Sin él, un aviso. */
  reportes?: ReactNode;
  /** Contenido de la subpestaña Cobro (precios del portal). Lo conecta la HU de precios; sin él, un aviso. */
  cobro?: ReactNode;
  /** Contenido de la subpestaña Caja (usuarios y cobros de caja). Sin él, la pestaña no aparece. Admin OT: Cobrar, Cobros del día y Usuarios de caja; SuperAdmin: Usuarios y Cobros de caja. */
  caja?: ReactNode;
  /** Sección de dominio de Diseño (llega con otra HU). */
  domain?: ReactNode;
  /** Subpestaña inicial. Por defecto Reportes, la primera. */
  initialTab?: PortalPnTabId;
  /**
   * Muestra SOLO esa sección, sin la fila de pestañas: la sección la elige el submenú «Portal Persona Natural» del menú de
   * abajo (Reportes, Cobro, Caja, Diseño) y viaja en la URL (`?tab=`). Sin ella, el componente pinta las pestañas de siempre.
   */
  seccion?: PortalPnTabId;
  /** Pestaña activa controlada desde fuera (p. ej. la página, según `?tab=`). Sin ella, el componente lleva su propio estado. */
  activeTab?: PortalPnTabId;
  /** Avisa del cambio de pestaña cuando la controla la página. */
  onTabChange?: (id: PortalPnTabId) => void;
}

/**
 * Subpestañas del Portal Persona Natural del organismo (Épica #14297): `Reportes` (la primera), `Cobro`, `Caja` y `Diseño`.
 * Es el mismo componente para el administrador del organismo y para el SuperAdmin: todo se ancla a `transitOfficeId`.
 */
export function PortalPnTabs({ transitOfficeId, reportes, cobro, caja, domain, initialTab, seccion, activeTab, onTabChange }: PortalPnTabsProps) {
  const [localActive, setLocalActive] = useState<PortalPnTabId>(initialTab ?? "reportes");
  const active = activeTab ?? localActive;
  const setActive = (id: PortalPnTabId) => {
    if (activeTab === undefined) setLocalActive(id);
    onTabChange?.(id);
  };

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
    ...(caja ? [{ id: "caja" as const, label: "Caja", content: caja }] : []),
    {
      id: "diseno",
      label: "Diseño",
      content: <PortalPnDesignSection transitOfficeId={transitOfficeId} domain={domain} />,
    },
  ];

  if (seccion) {
    const elegida = tabs.find((t) => t.id === seccion) ?? tabs[0];
    return <section aria-label={elegida.label}>{elegida.content}</section>;
  }
  return <SectionTabs tabs={tabs} active={active} onChange={setActive} ariaLabel="Secciones del portal Persona Natural" />;
}

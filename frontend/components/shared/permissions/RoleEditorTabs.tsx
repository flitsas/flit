"use client";

import type { ReactNode } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";

// HU #13653 (Épica #12753) — sub-pestañas de los editores de rol (Panel SA y roles propios de la compañía):
// «Datos del rol | Módulos | Tipos de servicio». Mismo componente de pestañas que Mandatos (SectionTabs: ARIA y teclado).
// Solo organiza la vista: el formulario que las contiene sigue guardando todo junto.

export type RoleEditorTabId = "datos" | "modulos" | "tipos";

export interface RoleEditorTabsProps {
  /** Campos del rol (código, nombre, descripción…). Sin ellos no hay pestaña «Datos del rol». */
  data?: ReactNode;
  /** Permisos de los módulos que no son tipos de servicio. */
  modules: ReactNode;
  /** Permisos por tipo de servicio (Ver / Crear). `null` o ausente: no hay pestaña «Tipos de servicio». */
  services?: ReactNode | null;
  active: RoleEditorTabId;
  onChange: (id: RoleEditorTabId) => void;
  ariaLabel?: string;
}

/**
 * Con una sola sección (sin datos y sin tipos de servicio) no se muestran pestañas: solo la lista de módulos.
 */
export function RoleEditorTabs({
  data,
  modules,
  services,
  active,
  onChange,
  ariaLabel = "Secciones del rol",
}: RoleEditorTabsProps) {
  const tabs: SectionTab<RoleEditorTabId>[] = [];
  if (data) tabs.push({ id: "datos", label: "Datos del rol", content: data });
  tabs.push({ id: "modulos", label: "Módulos", content: modules });
  if (services) tabs.push({ id: "tipos", label: "Tipos de servicio", content: services });

  if (tabs.length === 1) return <>{modules}</>;

  const current = tabs.some((t) => t.id === active) ? active : tabs[0].id;
  return <SectionTabs tabs={tabs} active={current} onChange={onChange} ariaLabel={ariaLabel} />;
}

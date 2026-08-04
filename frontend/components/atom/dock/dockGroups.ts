import type { ComponentType, CSSProperties } from "react";
import {
  FileStack,
  ShieldCheck,
  BarChart3,
  Users,
  Building2,
  Landmark,
  Lock,
  Radar,
  HelpCircle,
  FolderCog,
  Tag,
} from "lucide-react";
import { OT_ADM_DOCK } from "@/components/admin/transit-offices/ot-nav";

/**
 * Agrupadores del dock — orden estable de las píldoras.
 * Trámites e Identidad son grupos de un solo ítem → píldora directa (no submenú).
 * Dashboard no se lista: el FAB central abre Inicio.
 * `administracion` / `preasignacion`: Admin OT (pestañas hub → dock).
 */
export const DOCK_GROUP_ORDER = [
  "tramites",
  "preasignacion",
  "identidad",
  "reportes",
  "usuarios",
  "administracion",
  "companias",
  "ot",
  "administradores",
  "soporte",
  "ayuda",
] as const;

export type DockGroupId = (typeof DOCK_GROUP_ORDER)[number];

export const DOCK_GROUP_LABEL: Record<DockGroupId, string> = {
  tramites: "Trámites",
  identidad: "Identidad",
  reportes: "Reportes",
  usuarios: "Usuarios",
  preasignacion: "Preasignación",
  administracion: "Administración",
  companias: "Compañías",
  ot: "OT",
  administradores: "Administradores",
  soporte: "Soporte",
  ayuda: "Ayuda",
};

/**
 * Firma mínima de los iconos del dock (lucide-react). `aria-hidden` admite también la forma
 * string porque así se escribe en JSX (`aria-hidden="true"`).
 */
export type DockIconComponent = ComponentType<{
  className?: string;
  style?: CSSProperties;
  strokeWidth?: number;
  "aria-hidden"?: boolean | "true" | "false";
}>;

export const DOCK_GROUP_ICON: Record<DockGroupId, DockIconComponent> = {
  tramites: FileStack,
  identidad: ShieldCheck,
  reportes: BarChart3,
  usuarios: Users,
  preasignacion: Tag,
  administracion: FolderCog,
  companias: Building2,
  ot: Landmark,
  administradores: Lock,
  soporte: Radar,
  ayuda: HelpCircle,
};

/** Mapeo entrada del dock → agrupador (por key estable). */
export const DOCK_ITEM_GROUP: Record<string, DockGroupId> = {
  tramites: "tramites",
  // Módulo id sigue siendo `validaciones` en la SPA; el label visible es "Identidad".
  validaciones: "identidad",
  reportes: "reportes",
  "reportes-detallados": "reportes",
  usuarios: "usuarios",
  "admin-companies": "companias",
  "mi-empresa": "companias",
  "admin-documents": "companias",
  "admin-improntas": "companias",
  "admin-quipux": "companias",
  "admin-transit": "ot",
  // Admin OT — pestañas hub en el dock
  [OT_ADM_DOCK.tramites]: "tramites",
  [OT_ADM_DOCK.rules]: "administracion",
  [OT_ADM_DOCK.documents]: "administracion",
  [OT_ADM_DOCK.requirements]: "administracion",
  [OT_ADM_DOCK.preasignacion]: "preasignacion",
  [OT_ADM_DOCK.usuarios]: "usuarios",
  [OT_ADM_DOCK.reportes]: "reportes",
  rbac: "administradores",
  auditoria: "administradores",
  "log-qx": "soporte",
  "ict-logs": "soporte",
  ayuda: "ayuda",
};

export type DockEntryLike = {
  key: string;
  label: string;
  icon: DockIconComponent;
  active: boolean;
  onClick: () => void;
};

export type DockGroupView = {
  id: DockGroupId;
  label: string;
  icon: (typeof DOCK_GROUP_ICON)[DockGroupId];
  items: DockEntryLike[];
  /** Algún ítem del grupo está activo. */
  active: boolean;
};

/** Agrupa entradas visibles; omite agrupadores vacíos. Ítems sin mapa van a un cubo final no listado (no deberían existir). */
export function buildDockGroups(entries: DockEntryLike[]): DockGroupView[] {
  const buckets = new Map<DockGroupId, DockEntryLike[]>();
  for (const id of DOCK_GROUP_ORDER) buckets.set(id, []);

  for (const it of entries) {
    const gid = DOCK_ITEM_GROUP[it.key];
    if (!gid) continue;
    buckets.get(gid)!.push(it);
  }

  return DOCK_GROUP_ORDER.map((id) => {
    const items = buckets.get(id) ?? [];
    return {
      id,
      label: DOCK_GROUP_LABEL[id],
      icon: DOCK_GROUP_ICON[id],
      items,
      active: items.some((i) => i.active),
    };
  }).filter((g) => g.items.length > 0);
}

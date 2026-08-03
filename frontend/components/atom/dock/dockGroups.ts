import type { ComponentType, CSSProperties } from "react";
import {
  LayoutGrid,
  BarChart3,
  Users,
  Building2,
  Landmark,
  Lock,
  Radar,
  HelpCircle,
} from "lucide-react";

/** Agrupadores del dock — orden estable de las píldoras. */
export const DOCK_GROUP_ORDER = [
  "operacion",
  "reportes",
  "usuarios",
  "companias",
  "ot",
  "administradores",
  "soporte",
  "ayuda",
] as const;

export type DockGroupId = (typeof DOCK_GROUP_ORDER)[number];

export const DOCK_GROUP_LABEL: Record<DockGroupId, string> = {
  operacion: "Operación",
  reportes: "Reportes",
  usuarios: "Usuarios",
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
  operacion: LayoutGrid,
  reportes: BarChart3,
  usuarios: Users,
  companias: Building2,
  ot: Landmark,
  administradores: Lock,
  soporte: Radar,
  ayuda: HelpCircle,
};

/** Mapeo entrada del dock → agrupador (por key estable). */
export const DOCK_ITEM_GROUP: Record<string, DockGroupId> = {
  dashboard: "operacion",
  tramites: "operacion",
  validaciones: "operacion",
  reportes: "reportes",
  "reportes-detallados": "reportes",
  usuarios: "usuarios",
  "admin-companies": "companias",
  "mi-empresa": "companias",
  "admin-documents": "companias",
  "admin-improntas": "companias",
  "admin-quipux": "companias",
  "admin-transit": "ot",
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

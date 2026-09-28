// Catálogo de navegación de un producto (GUIA-DOCK-INFERIOR-FLOTANTE.md, Contratos A y B; contrato de plataforma §8).
// El catálogo dice QUÉ existe y QUIÉN lo ve; el dock solo lo dibuja. SuiteShell no conoce ningún producto: cada
// producto le entrega su catálogo.
import type { ComponentType } from "react";

export type NavIcon = ComponentType<{ className?: string; strokeWidth?: number; "aria-hidden"?: boolean | "true" }>;

export interface NavSection {
  id: string;
  label: string;
  icon: NavIcon;
  /** Lado del botón de inicio en el dock. Declarado, no por mitades (HU #12723). */
  side: "left" | "right";
}

export interface NavItem {
  key: string;
  label: string;
  /** Ruta interna del producto o URL absoluta (otro producto de la suite). */
  href: string;
  section: string;
  icon: NavIcon;
  /** Permiso del token que lo muestra. Sin permiso, lo ve todo el que entra al producto. */
  permission?: string;
  /** Solo para quien tenga alguno de estos roles del token (además del SuperAdmin). */
  roles?: string[];
  /** Solo para el SuperAdmin. */
  superAdminOnly?: boolean;
  /** Un nivel de submenú (p. ej. Administradores → Plataforma → Mandatos). */
  children?: NavItem[];
}

/** El orden del dock sale de `sections`, nunca del orden de `items` (guía §2, Contrato A). */
export interface NavCatalog {
  sections: NavSection[];
  items: NavItem[];
}

/** Quién mira el menú: los permisos del token del producto (`@flit/auth` SessionUser). */
export interface NavViewer {
  permissions: string[];
  isSuperAdmin: boolean;
  /** Códigos de rol del token; solo hacen falta si el catálogo filtra por rol. */
  roles?: string[];
}

export interface DockEntry {
  key: string;
  label: string;
  href: string;
  icon: NavIcon;
  active: boolean;
  children?: DockEntry[];
}

export interface DockGroup {
  id: string;
  label: string;
  icon: NavIcon;
  side: "left" | "right";
  active: boolean;
  items: DockEntry[];
}

export function canSee(item: NavItem, viewer: NavViewer): boolean {
  if (viewer.isSuperAdmin) return true;
  if (item.superAdminOnly) return false;
  if (item.roles && !item.roles.some((r) => viewer.roles?.includes(r))) return false;
  return !item.permission || viewer.permissions.includes(item.permission);
}

/** Ruta activa por prefijo más largo (guía §2): `/ventas/informes/anual` gana a `/ventas`. */
export function isActive(href: string, pathname: string): boolean {
  if (/^https?:\/\//.test(href)) return false;
  const path = href.split("?")[0];
  return path === "/" ? pathname === "/" : pathname === path || pathname.startsWith(`${path}/`);
}

export function activeKeyForPath(items: NavItem[], pathname: string): string | null {
  const flat = items.flatMap((it) => [it, ...(it.children ?? [])]);
  const matches = flat.filter((it) => isActive(it.href, pathname)).sort((a, b) => b.href.length - a.href.length);
  return matches[0]?.key ?? null;
}

/**
 * Contrato B en un solo sitio: filtra por permisos, agrupa por sección en el orden declarado, descarta secciones
 * vacías y marca lo activo. Todas las navegaciones (dock, móvil) deben usar esto.
 */
export function buildDock(catalog: NavCatalog, viewer: NavViewer, pathname: string): DockGroup[] {
  const visible = catalog.items
    .filter((it) => canSee(it, viewer))
    .map((it) => ({ ...it, children: it.children?.filter((c) => canSee(c, viewer)) }))
    .filter((it) => !it.children || it.children.length > 0 || it.href);
  const activeKey = activeKeyForPath(visible, pathname);

  const toEntry = (it: NavItem): DockEntry => ({
    key: it.key,
    label: it.label,
    href: it.href,
    icon: it.icon,
    active: it.key === activeKey,
    children: it.children?.length ? it.children.map(toEntry) : undefined,
  });

  return catalog.sections
    .map((section) => {
      const items = visible.filter((it) => it.section === section.id).map(toEntry);
      const active = items.some((e) => e.active || e.children?.some((c) => c.active));
      return { id: section.id, label: section.label, icon: section.icon, side: section.side, active, items };
    })
    .filter((g) => g.items.length > 0);
}

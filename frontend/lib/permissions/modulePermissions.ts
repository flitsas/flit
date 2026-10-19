import type { AccessibleModule } from "@/lib/api/security";
import type { GrantablePermissionItem, UserPermissionOverride } from "@/lib/api/permission-overrides";

// Lógica pura de la sección «Permisos por módulo» (HU #13652). Sin React: se prueba aparte.

/** Origen del estado de una acción o de un módulo. */
export type PermissionOrigin = "inherited" | "individual" | "partial";

export interface ActionView {
  id: string;
  slug: string;
  name: string;
  /** `inherited` = lo dicta el rol; `individual` = hay un permiso individual (allow/deny). */
  origin: "inherited" | "individual";
  /** ¿El usuario tiene hoy el permiso? (rol + individual: deny gana, allow suma). */
  enabled: boolean;
  /** Efecto individual vigente, si existe. */
  effect?: "allow" | "deny";
  grantedByName?: string | null;
  grantedAt?: string | null;
  /** HU #13653: permiso exclusivo del Super Admin frente al usuario destino (ningún administrador de su compañía/OT lo tiene). */
  superAdminOnly: boolean;
}

export interface ModuleView {
  id: string;
  code: string;
  name: string;
  productCode?: string;
  origin: PermissionOrigin;
  actions: ActionView[];
  /** Cuántas acciones tiene habilitadas el usuario. */
  enabledCount: number;
}

/**
 * Cruza el catálogo de módulos (la MISMA fuente que RbacAdmin: `getAccessibleModules`, mismo orden y
 * nombres) con lo otorgable, lo heredado del rol y los permisos individuales.
 *
 * AC2: solo se listan permisos exigidos. La lista otorgable ya trae solo `enforced = 'true'`; se aplica el
 * mismo filtro a lo heredado (una acción que no es otorgable ni tiene override no aparece), y los módulos
 * que quedan sin acciones se omiten.
 */
export function buildModuleViews(
  modules: AccessibleModule[],
  grantable: GrantablePermissionItem[],
  roleGrantedIds: ReadonlySet<string>,
  overrides: UserPermissionOverride[],
): ModuleView[] {
  const grantableIds = new Set(grantable.map((g) => g.id));
  const superAdminOnlyIds = new Set(grantable.filter((g) => g.superAdminOnly).map((g) => g.id));
  const overrideById = new Map(overrides.map((o) => [o.permissionId, o]));

  const views: ModuleView[] = [];
  for (const m of modules) {
    const actions: ActionView[] = [];
    for (const a of m.actions) {
      const override = overrideById.get(a.id);
      if (!grantableIds.has(a.id) && !override) continue;
      const base = { id: a.id, slug: a.slug, name: a.name, superAdminOnly: superAdminOnlyIds.has(a.id) };
      if (override) {
        actions.push({
          ...base,
          origin: "individual",
          effect: override.effect,
          enabled: override.effect === "allow",
          grantedByName: override.grantedByName,
          grantedAt: override.grantedAt,
        });
      } else {
        actions.push({ ...base, origin: "inherited", enabled: roleGrantedIds.has(a.id) });
      }
    }
    if (actions.length === 0) continue;
    const individual = actions.filter((a) => a.origin === "individual").length;
    views.push({
      id: m.id,
      code: m.code,
      name: m.name,
      productCode: m.productCode,
      origin: individual === 0 ? "inherited" : individual === actions.length ? "individual" : "partial",
      actions,
      enabledCount: actions.filter((a) => a.enabled).length,
    });
  }
  return views;
}

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
  /**
   * HU #13655 (FD2-1..3): el permiso está fuera del tope de quien consulta (no está en `/grantable`) pero el
   * usuario lo tiene por un permiso individual (o lo tuvo en esta sesión). Se puede quitar, no volver a otorgar.
   */
  outOfCap?: boolean;
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
  /**
   * HU #13655: ids que ya salieron del tope y se siguen mostrando (deshabilitados) aunque ya no tengan permiso
   * individual — p. ej. tras quitarlo. Sin esto la acción desaparecería de la lista en vez de quedar bloqueada.
   */
  lockedIds: ReadonlySet<string> = new Set(),
): ModuleView[] {
  const grantableIds = new Set(grantable.map((g) => g.id));
  const superAdminOnlyIds = new Set(grantable.filter((g) => g.superAdminOnly).map((g) => g.id));
  const overrideById = new Map(overrides.map((o) => [o.permissionId, o]));

  const views: ModuleView[] = [];
  for (const m of modules) {
    const actions: ActionView[] = [];
    for (const a of m.actions) {
      const override = overrideById.get(a.id);
      const outOfCap = !grantableIds.has(a.id);
      if (outOfCap && !override && !lockedIds.has(a.id)) continue;
      const base = {
        id: a.id,
        slug: a.slug,
        name: a.name,
        superAdminOnly: superAdminOnlyIds.has(a.id),
        ...(outOfCap ? { outOfCap: true } : {}),
      };
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

/**
 * HU #13655 (FD2-3): el catálogo de módulos es el de quien consulta, así que un permiso individual fuera de su tope no viene
 * en él. Se agrega a su módulo con el nombre que trae el permiso individual, para mostrarlo con «Fuera de tu tope». Las
 * acciones ya agregadas se conservan (al quitarlo, la acción queda bloqueada en vez de desaparecer).
 */
export function withOverrideActions(modules: AccessibleModule[], overrides: UserPermissionOverride[]): AccessibleModule[] {
  const known = new Set(modules.flatMap((m) => m.actions.map((a) => a.id)));
  const extra = overrides.filter((o) => !known.has(o.permissionId) && o.moduleCode);
  if (extra.length === 0) return modules;
  const result = modules.map((m) => ({ ...m, actions: [...m.actions] }));
  for (const o of extra) {
    let module = result.find((m) => m.code === o.moduleCode);
    if (!module) {
      module = { id: o.moduleCode!, code: o.moduleCode!, name: o.moduleCode!, sortOrder: Number.MAX_SAFE_INTEGER, actions: [], productCode: o.productCode };
      result.push(module);
    }
    module.actions.push({ id: o.permissionId, slug: o.slug, name: o.name ?? o.slug });
  }
  return result;
}

/** Ids de los permisos individuales que quedan fuera del tope (`grantable`) de quien consulta. */
export function outOfCapOverrideIds(
  grantable: GrantablePermissionItem[],
  overrides: UserPermissionOverride[],
): string[] {
  const grantableIds = new Set(grantable.map((g) => g.id));
  return overrides.filter((o) => !grantableIds.has(o.permissionId)).map((o) => o.permissionId);
}

// ── HU #13656 (FD2-4): administradores en solo lectura ───────────────────────────────────────────────────

export const ADMIN_READ_ONLY_MESSAGE = "Solo el Super Admin puede modificar los permisos de un administrador.";

/** «Administrador» según el backend (`AdministratorRoles`): SuperAdmin, AdminCompany, ot_admin y admin_tramites. */
const ADMINISTRATOR_ROLE_CODES = new Set(["superadmin", "admincompany", "ot_admin", "admin_tramites"]);

export function isAdministratorRoleCode(roleCode: string | null | undefined): boolean {
  return ADMINISTRATOR_ROLE_CODES.has((roleCode ?? "").trim().toLowerCase());
}

/**
 * Modo `admin`: la ficha de otro administrador, o la propia, se ve en solo lectura (el backend rechaza con
 * TARGET_IS_ADMIN / SELF). El Super Admin (`superadmin`) no se ve afectado por esta regla.
 */
export function adminReadOnlyFor(
  mode: "superadmin" | "admin",
  targetRoleCodes: ReadonlyArray<string | null | undefined> | undefined,
  isSelf: boolean | undefined,
): boolean {
  if (mode !== "admin") return false;
  return Boolean(isSelf) || (targetRoleCodes ?? []).some(isAdministratorRoleCode);
}

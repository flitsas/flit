import type { AccessibleModule } from "@/lib/api/security";

/** Módulos del organismo de tránsito: código `ot-*` o acciones `ot.*` (mismo criterio que la API). */
export function isOtModule(m: AccessibleModule): boolean {
  return m.code.startsWith("ot-") || m.actions.some((a) => a.slug.startsWith("ot."));
}

/**
 * Los permisos OT solo tienen efecto en un organismo de tránsito: un rol de compañía no los ofrece
 * (misma decisión que en los permisos por usuario, OT_NOT_APPLICABLE).
 */
export function modulesForRoleTarget(modules: AccessibleModule[], targetEntityType: string): {
  modules: AccessibleModule[];
  hiddenIds: Set<string>;
} {
  if (targetEntityType !== "COMPANY") return { modules, hiddenIds: new Set() };
  const hidden = modules.filter(isOtModule);
  return {
    modules: modules.filter((m) => !isOtModule(m)),
    hiddenIds: new Set(hidden.flatMap((m) => m.actions.map((a) => a.id))),
  };
}

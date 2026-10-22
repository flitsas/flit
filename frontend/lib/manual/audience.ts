import type { ManualAudience, ManualProfile } from "./types";

/**
 * HU-F — qué audiencias del manual ve cada perfil cuando pregunta desde DR. FLIT.
 *
 *  - gestor: lo común y lo del Gestor.
 *  - admin_company: además la consola de administración de su compañía (sigue operando como gestor).
 *  - ot_admin: lo común y lo del Organismo de Tránsito. No ve el flujo del Gestor: es otro producto.
 *  - superadmin: todo (opera y da soporte sobre cualquier consola).
 */
const AUDIENCES_BY_PROFILE: Record<ManualProfile, readonly ManualAudience[]> = {
  gestor: ["Todos", "Gestor"],
  admin_company: ["Todos", "Gestor", "Admin de Compañía"],
  ot_admin: ["Todos", "Organismo de Tránsito"],
  superadmin: ["Todos", "Gestor", "Organismo de Tránsito", "Admin de Compañía", "Super Admin"],
};

export function visibleAudiences(profile: ManualProfile): readonly ManualAudience[] {
  return AUDIENCES_BY_PROFILE[profile];
}

export function isVisibleFor(
  audience: ManualAudience,
  audiences: readonly ManualAudience[] | undefined,
): boolean {
  return audiences == null || audiences.includes(audience);
}

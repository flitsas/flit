/**
 * Perfiles funcionales de la plataforma (context/contexto-perfiles.md): FLIT, Gestor y
 * Organismo de Tránsito. Los códigos son los MISMOS que emite el backend en
 * `TenantUserDto.Profile` (ver Flit.Api/Authorization/UserProfiles.cs).
 *
 * El perfil lo resuelve el servidor. `inferProfile` existe solo como respaldo para las
 * respuestas que todavía no lo traen (p. ej. el listado de usuarios OT) y para no romper
 * si un despliegue de frontend va por delante del de la API.
 */

export type UserProfileKind = "FLIT" | "GESTOR" | "OT";

export const USER_PROFILE_LABEL: Record<UserProfileKind, string> = {
  FLIT: "FLIT",
  GESTOR: "Gestor",
  OT: "Organismo de Tránsito",
};

/** Label corto para chips en tablas densas. */
export const USER_PROFILE_SHORT_LABEL: Record<UserProfileKind, string> = {
  FLIT: "FLIT",
  GESTOR: "Gestor",
  OT: "OT",
};

/** Descripción de una línea, para los selectores de creación de usuario. */
export const USER_PROFILE_DESCRIPTION: Record<UserProfileKind, string> = {
  FLIT: "Equipo interno de FLIT. No pertenece a ninguna compañía ni organismo.",
  GESTOR: "Empresa cliente que radica trámites.",
  OT: "Funcionarios de un Organismo de Tránsito.",
};

export const USER_PROFILE_ORDER: UserProfileKind[] = ["FLIT", "GESTOR", "OT"];

/** Tipo de entidad de los roles asignables a cada perfil (`security.roles.target_entity_type`). */
export type RoleTargetEntityType = "COMPANY" | "TRANSIT_OFFICE";

export function targetEntityTypeForProfile(profile: UserProfileKind): RoleTargetEntityType {
  return profile === "OT" ? "TRANSIT_OFFICE" : "COMPANY";
}

/** El rol de sistema que define el perfil FLIT (`security.roles.code`). */
export const SUPER_ADMIN_ROLE_CODE = "superadmin";

export function isSuperAdminRole(roleCode: string | null | undefined): boolean {
  return (roleCode ?? "").trim().toLowerCase() === SUPER_ADMIN_ROLE_CODE;
}

/**
 * Roles que se le pueden ofrecer a un usuario de este perfil.
 *
 * El catálogo COMPANY incluye el rol SuperAdmin —no tiene un `target_entity_type` propio porque
 * el CHECK de BD solo admite COMPANY | TRANSIT_OFFICE—, así que sin este filtro aparecía como
 * opción al editar a un Gestor. El backend también lo rechaza (SUPERADMIN_ROLE_NOT_ASSIGNABLE);
 * esto es para no llegar a ofrecerlo.
 */
export function selectableRolesForProfile<T extends { code: string }>(
  roles: readonly T[],
  profile: UserProfileKind,
): T[] {
  return roles.filter((role) =>
    profile === "FLIT" ? isSuperAdminRole(role.code) : !isSuperAdminRole(role.code),
  );
}

function isProfileKind(value: string): value is UserProfileKind {
  return value === "FLIT" || value === "GESTOR" || value === "OT";
}

/**
 * Respaldo cuando el backend no informó el perfil. Deliberadamente conservador: solo los dos
 * roles de sistema son concluyentes por sí mismos; para el resto decide el tipo de tenant.
 */
export function inferProfile(
  roleCode: string | null | undefined,
  tenantType?: string | null,
): UserProfileKind {
  const code = (roleCode ?? "").trim().toLowerCase();
  if (code === "superadmin") return "FLIT";
  if (code === "ot_admin") return "OT";
  if ((tenantType ?? "").toUpperCase() === "TRANSIT_OFFICE") return "OT";
  return "GESTOR";
}

/**
 * Perfil de un usuario: prioriza el valor calculado por el servidor y solo infiere si falta.
 * Antes se infería SIEMPRE en el cliente, y cualquier rol personalizado de un tenant OT
 * terminaba etiquetado como Gestor.
 */
export function resolveProfile(user: {
  profile?: string | null;
  roleCode?: string | null;
  tenantType?: string | null;
}): UserProfileKind {
  const fromServer = (user.profile ?? "").trim().toUpperCase();
  if (isProfileKind(fromServer)) return fromServer;
  return inferProfile(user.roleCode, user.tenantType);
}

export function profileLabel(profile: UserProfileKind): string {
  return USER_PROFILE_LABEL[profile];
}

export function profileShortLabel(profile: UserProfileKind): string {
  return USER_PROFILE_SHORT_LABEL[profile];
}

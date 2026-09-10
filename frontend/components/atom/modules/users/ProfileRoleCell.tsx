"use client";

import {
  profileLabel,
  profileShortLabel,
  resolveProfile,
  type UserProfileKind,
} from "@/lib/users/profiles";

const PROFILE_STYLE: Record<UserProfileKind, { bg: string; color: string }> = {
  FLIT: { bg: "rgba(85,126,255,0.12)", color: "#557EFF" },
  GESTOR: { bg: "rgba(0,219,213,0.12)", color: "#0AA8A3" },
  OT: { bg: "rgba(249,172,0,0.14)", color: "#8a6000" },
};

/**
 * Chip del perfil. Compartido con EditUserModal para que la tabla y el modal no diverjan.
 * `full` usa el nombre completo del perfil ("Organismo de Tránsito"), que cabe en un modal
 * pero no en una celda de tabla.
 */
export function ProfileBadge({ profile, full = false }: { profile: UserProfileKind; full?: boolean }) {
  const style = PROFILE_STYLE[profile];
  return (
    <span
      className="inline-flex w-fit max-w-full truncate rounded-md px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide"
      style={{ background: style.bg, color: style.color }}
    >
      {full ? profileLabel(profile) : profileShortLabel(profile)}
    </span>
  );
}

export interface ProfileRoleCellProps {
  roleCode: string | null | undefined;
  roleName: string | null | undefined;
  /** Perfil ya calculado por el backend. Manda sobre cualquier inferencia local. */
  profile?: string | null;
  /** Respaldo cuando el backend no informó el perfil. */
  tenantType?: string | null;
}

/**
 * Celda "Perfil" de las tablas de usuarios. Perfil (FLIT / Gestor / OT) es un eje distinto del
 * rol: cualquier usuario de una compañía es perfil Gestor, sea Administrador de Compañía o
 * Radicador — separar la columna evita que el chip domine visualmente sobre el rol real
 * (HU #11551).
 */
export function ProfileCell({ roleCode, profile, tenantType }: Omit<ProfileRoleCellProps, "roleName">) {
  const kind = resolveProfile({ profile, roleCode, tenantType });
  return (
    <div className="flex min-w-0 items-center">
      <ProfileBadge profile={kind} />
    </div>
  );
}

/** Celda "Rol" de las tablas de usuarios: texto legible, en su propia columna. */
export function RoleCell({ roleCode, roleName }: Pick<ProfileRoleCellProps, "roleCode" | "roleName">) {
  const roleLabel = roleName?.trim() || roleCode?.trim() || "Sin rol";
  return (
    <span className="block truncate text-xs font-medium" title={roleLabel}>
      {roleLabel}
    </span>
  );
}

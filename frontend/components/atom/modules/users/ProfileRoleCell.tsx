"use client";

import {
  isAdminRole,
  profileLabel,
  profileShortLabel,
  resolveProfile,
  type UserProfileKind,
} from "@/lib/users/profiles";

// El perfil es un dato de clasificación, no un estado: el cian de Gestor se leía como «éxito», así
// que usa el tono neutral tintado de la paleta de badges (FLIT = info, OT = warning).
const PROFILE_TONE: Record<UserProfileKind, "info" | "neutral" | "warning"> = {
  FLIT: "info",
  GESTOR: "neutral",
  OT: "warning",
};

/**
 * Chip del perfil. Compartido con EditUserModal para que la tabla y el modal no diverjan.
 * `full` usa el nombre completo del perfil ("Organismo de Tránsito"), que cabe en un modal
 * pero no en una celda de tabla.
 */
export function ProfileBadge({
  profile,
  full = false,
  admin = false,
}: {
  profile: UserProfileKind;
  full?: boolean;
  /** Bug #13055 — añade «· Admin» al chip del administrador de la compañía u organismo. */
  admin?: boolean;
}) {
  const tone = PROFILE_TONE[profile];
  const base = full ? profileLabel(profile) : profileShortLabel(profile);
  return (
    <span
      className="inline-flex w-fit max-w-full truncate rounded-md border px-1.5 py-0.5 text-xs font-semibold uppercase tracking-wide"
      style={{
        background: `var(--badge-${tone}-bg)`,
        color: `var(--badge-${tone}-fg)`,
        borderColor: `var(--badge-${tone}-border)`,
      }}
    >
      {admin ? `${base} · Admin` : base}
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
      <ProfileBadge profile={kind} admin={isAdminRole(roleCode)} />
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

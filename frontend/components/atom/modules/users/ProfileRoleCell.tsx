"use client";

import { profileShortLabel, resolveProfile, type UserProfileKind } from "@/lib/users/profiles";

const PROFILE_STYLE: Record<UserProfileKind, { bg: string; color: string }> = {
  FLIT: { bg: "rgba(85,126,255,0.12)", color: "#557EFF" },
  GESTOR: { bg: "rgba(0,219,213,0.12)", color: "#0AA8A3" },
  OT: { bg: "rgba(249,172,0,0.14)", color: "#8a6000" },
};

export interface ProfileRoleCellProps {
  roleCode: string | null | undefined;
  roleName: string | null | undefined;
  /** Perfil ya calculado por el backend. Manda sobre cualquier inferencia local. */
  profile?: string | null;
  /** Respaldo cuando el backend no informó el perfil. */
  tenantType?: string | null;
}

/** Celda compuesta Perfil + Rol de las tablas de usuarios. */
export function ProfileRoleCell({ roleCode, roleName, profile, tenantType }: ProfileRoleCellProps) {
  const kind = resolveProfile({ profile, roleCode, tenantType });
  const style = PROFILE_STYLE[kind];
  const roleLabel = roleName?.trim() || roleCode?.trim() || "Sin rol";

  return (
    <div className="flex min-w-0 flex-col gap-1">
      <span
        className="inline-flex w-fit max-w-full truncate rounded-md px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide"
        style={{ background: style.bg, color: style.color }}
      >
        {profileShortLabel(kind)}
      </span>
      <span className="truncate text-[11px] opacity-80" title={roleLabel}>
        {roleLabel}
      </span>
    </div>
  );
}

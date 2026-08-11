"use client";

import { useEffect, useMemo, useState } from "react";
import { Landmark, Shield, ShieldCheck, X } from "lucide-react";
import { createInvitation, type TenantRole } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { fetchTransitOfficeTenants, type TransitOfficeTenantItem } from "@/lib/api/admin-transit-office-tenants";
import type { CompanyListItem } from "@/lib/api/types";
import { superadminClient } from "@/lib/api/superadmin-client";
import { SearchableSelect } from "@/components/atom/SearchableSelect";
import {
  USER_PROFILE_DESCRIPTION,
  USER_PROFILE_LABEL,
  USER_PROFILE_ORDER,
  targetEntityTypeForProfile,
  type UserProfileKind,
} from "@/lib/users/profiles";

const SUPER_ADMIN_ROLE_CODE = "superadmin";

const PROFILE_ICON: Record<UserProfileKind, typeof Shield> = {
  FLIT: ShieldCheck,
  GESTOR: Shield,
  OT: Landmark,
};

export interface InviteUserModalProps {
  onClose: () => void;
  onSuccess: () => void;
  isSuperAdmin: boolean;
  /** Roles del propio tenant — los usa AdminCompany / ot_admin (GET /security/roles). */
  roles: TenantRole[];
  rolesLoading: boolean;
  /**
   * Alcance fijo: la ficha de una compañía o el hub de un organismo. Se oculta el selector de
   * destino, pero el de ROL se sigue mostrando.
   */
  fixedTarget?: { tenantId: string; profile: Exclude<UserProfileKind, "FLIT">; name?: string };
}

/**
 * Alta de usuario para los tres perfiles (context/usuarios-contex.md). El SuperAdmin elige
 * PERFIL y luego ROL entre los aplicables a ese perfil; antes el rol lo forzaba el backend
 * según el tenant destino y la UI no ofrecía ninguna selección.
 */
export function InviteUserModal({
  onClose,
  onSuccess,
  isSuperAdmin,
  roles,
  rolesLoading,
  fixedTarget,
}: InviteUserModalProps) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [profile, setProfile] = useState<UserProfileKind>(fixedTarget?.profile ?? "GESTOR");
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState(fixedTarget?.tenantId ?? "");
  const [companies, setCompanies] = useState<{ id: string; name: string }[]>([]);
  const [transitOfficeTenants, setTransitOfficeTenants] = useState<TransitOfficeTenantItem[]>([]);
  const [tenantsLoading, setTenantsLoading] = useState(false);
  const [catalogRoles, setCatalogRoles] = useState<TenantRole[]>([]);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [status, setStatus] = useState<"idle" | "loading" | "done" | "done_no_email">("idle");
  const [error, setError] = useState<string | null>(null);
  const [invitedEmail, setInvitedEmail] = useState("");

  const isFlitProfile = profile === "FLIT";
  const needsTenant = isSuperAdmin && !isFlitProfile && !fixedTarget;

  // Destinos disponibles: solo para el SuperAdmin y solo cuando el alcance no viene fijado.
  useEffect(() => {
    if (!isSuperAdmin || fixedTarget) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTenantsLoading(true);
    Promise.all([
      fetchCompaniesIndex({ pageSize: 200 }),
      fetchTransitOfficeTenants({ pageSize: 200 }),
    ])
      .then(([companiesResult, otResult]) => {
        setCompanies(companiesResult.data.map((c: CompanyListItem) => ({ id: c.id, name: c.razonSocial })));
        setTransitOfficeTenants(otResult.data);
      })
      .catch(() => {
        /* silencioso: el selector queda vacío y el submit valida */
      })
      .finally(() => setTenantsLoading(false));
  }, [isSuperAdmin, fixedTarget]);

  // Catálogo global de roles aplicables al perfil elegido (solo SuperAdmin: es quien puede
  // leer /superadmin/roles). AdminCompany / ot_admin usan los roles de su propio tenant.
  useEffect(() => {
    if (!isSuperAdmin) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setCatalogLoading(true);
    superadminClient
      .listRoles(targetEntityTypeForProfile(profile))
      .then((list) => {
        if (cancelled) return;
        setCatalogRoles(
          list
            .filter((r) => r.isActive)
            .map((r) => ({
              id: r.id,
              code: r.code,
              name: r.name,
              description: r.description,
              isSystem: r.isSystem,
              permissionCount: r.permissionCount,
              createdAt: r.createdAt,
            })),
        );
      })
      .catch(() => {
        if (!cancelled) setCatalogRoles([]);
      })
      .finally(() => {
        if (!cancelled) setCatalogLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [isSuperAdmin, profile]);

  /**
   * Roles ofrecidos. El rol de sistema SuperAdmin se excluye de Gestor/OT: es el perfil FLIT y
   * se asigna solo por esa vía, nunca como un rol más de una compañía.
   */
  const availableRoles = useMemo(() => {
    if (!isSuperAdmin) return roles;
    if (isFlitProfile) return [];
    return catalogRoles.filter((r) => r.code.toLowerCase() !== SUPER_ADMIN_ROLE_CODE);
  }, [isSuperAdmin, isFlitProfile, roles, catalogRoles]);

  const superAdminRoleId = useMemo(
    () => catalogRoles.find((r) => r.code.toLowerCase() === SUPER_ADMIN_ROLE_CODE)?.id ?? null,
    [catalogRoles],
  );

  const rolesBusy = isSuperAdmin ? catalogLoading : rolesLoading;
  const isDone = status === "done" || status === "done_no_email";

  // Un rol es obligatorio salvo para el SuperAdmin creando Gestor/OT, donde dejarlo vacío
  // significa "el de siempre" y el backend asigna el rol de sistema del tipo de tenant.
  const rolesRequiredAndMissing = !isSuperAdmin && selectedRoleIds.length === 0;

  function changeProfile(next: UserProfileKind) {
    setProfile(next);
    setSelectedRoleIds([]);
    setSelectedTenantId("");
    setError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (needsTenant && !selectedTenantId) {
      setError(
        profile === "OT"
          ? "Debes seleccionar el organismo de tránsito destino."
          : "Debes seleccionar la compañía destino.",
      );
      return;
    }

    if (rolesRequiredAndMissing) {
      setError("Selecciona al menos un rol para el usuario invitado.");
      return;
    }

    if (isSuperAdmin && isFlitProfile && !superAdminRoleId) {
      setError("No se encontró el rol de Super Administrador en el catálogo.");
      return;
    }

    // Perfil FLIT: un único rol de sistema y SIN tenant destino.
    const roleIds = isSuperAdmin && isFlitProfile ? [superAdminRoleId!] : selectedRoleIds;
    const targetTenantId = isSuperAdmin && !isFlitProfile
      ? fixedTarget?.tenantId || selectedTenantId
      : undefined;

    setStatus("loading");
    try {
      const result = await createInvitation(email.trim(), fullName.trim(), roleIds, targetTenantId);
      setInvitedEmail(result.email);
      setStatus(result.emailSent ? "done" : "done_no_email");
      onSuccess();
    } catch (err) {
      const apiErr = err as ApiError;
      const code = (apiErr.body as { code?: string } | undefined)?.code;
      setError(resolveErrorMessage(code, apiErr.status));
      setStatus("idle");
    }
  }

  const ProfileIcon = PROFILE_ICON[profile];

  return (
    <div className="fixed inset-0 z-50 grid place-items-center overflow-y-auto bg-black/40 px-4 py-6 backdrop-blur-sm">
      <div className="max-h-[90dvh] w-full max-w-md overflow-y-auto rounded-2xl border bg-white p-6 dark:bg-[#0B0F14]">
        <div className="mb-4 flex items-start justify-between">
          <div>
            <h3 className="text-lg font-bold">Invitar usuario</h3>
            <p className="mt-0.5 text-xs opacity-70">Asigna el acceso para colaborar dentro de FLIT.</p>
          </div>
          <button type="button" onClick={onClose} aria-label="Cerrar">
            <X className="h-5 w-5" />
          </button>
        </div>

        {isDone ? (
          <div className="space-y-3">
            <div
              className="rounded-xl border p-3"
              style={{ borderColor: "#00DBD5", background: "rgba(0,219,213,0.06)" }}
            >
              <p className="text-sm font-semibold" style={{ color: "#00DBD5" }}>
                Invitación enviada
              </p>
              <p className="mt-0.5 text-xs opacity-70">
                Se enviaron instrucciones de activación a <strong>{invitedEmail}</strong>.
              </p>
              {status === "done_no_email" && (
                <p className="mt-1.5 text-xs font-medium" style={{ color: "#F9AC00" }}>
                  El correo no pudo entregarse. El administrador puede reintentar más tarde.
                </p>
              )}
            </div>
            <OnboardingSteps />
            <button
              type="button"
              onClick={onClose}
              className="w-full rounded-xl py-2.5 text-sm font-semibold text-white"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              Cerrar
            </button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-3">
            {/* Perfil — solo el SuperAdmin elige; los demás administran su propio perfil. */}
            {isSuperAdmin && !fixedTarget && (
              <fieldset>
                <legend className="mb-1 block text-xs font-semibold">Perfil *</legend>
                <div className="flex flex-col gap-1.5">
                  {USER_PROFILE_ORDER.map((p) => {
                    const Icon = PROFILE_ICON[p];
                    const active = profile === p;
                    return (
                      <label
                        key={p}
                        className="flex cursor-pointer items-start gap-2 rounded-xl border px-3 py-2 text-xs transition"
                        style={{
                          borderColor: active ? "#557EFF" : "#DFE5ED",
                          background: active ? "rgba(85,126,255,0.06)" : undefined,
                        }}
                      >
                        <input
                          type="radio"
                          name="invite-profile"
                          value={p}
                          checked={active}
                          onChange={() => changeProfile(p)}
                          className="mt-0.5 h-3.5 w-3.5 accent-[#557EFF]"
                        />
                        <span className="min-w-0">
                          <span className="flex items-center gap-1.5 font-semibold">
                            <Icon className="h-3.5 w-3.5 shrink-0" style={{ color: "#557EFF" }} aria-hidden />
                            {USER_PROFILE_LABEL[p]}
                          </span>
                          <span className="mt-0.5 block opacity-70">{USER_PROFILE_DESCRIPTION[p]}</span>
                        </span>
                      </label>
                    );
                  })}
                </div>
              </fieldset>
            )}

            {isSuperAdmin && fixedTarget && (
              <div className="rounded-xl border px-3 py-2.5 text-xs" style={{ borderColor: "#DFE5ED" }}>
                <p className="text-[10px] font-semibold uppercase opacity-60">Destino</p>
                <p className="mt-0.5 flex items-center gap-1.5 font-semibold">
                  <ProfileIcon className="h-3.5 w-3.5 shrink-0" style={{ color: "#557EFF" }} aria-hidden />
                  {fixedTarget.name ?? USER_PROFILE_LABEL[fixedTarget.profile]}
                </p>
              </div>
            )}

            {/* Destino: el perfil FLIT no pertenece a ninguna compañía ni organismo. */}
            {needsTenant && (
              <div>
                {/* Buscador interno: la lista de destinos crece con cada empresa u OT dada de alta. */}
                <SearchableSelect
                  id="invite-tenant"
                  label={profile === "OT" ? "Organismo de tránsito destino *" : "Compañía destino *"}
                  options={
                    profile === "OT"
                      ? transitOfficeTenants.map((t) => ({
                          value: t.id,
                          label: t.legalName,
                          hint: t.transitOfficeCode,
                        }))
                      : companies.map((c) => ({ value: c.id, label: c.name }))
                  }
                  value={selectedTenantId}
                  onChange={setSelectedTenantId}
                  disabled={tenantsLoading}
                  placeholder={tenantsLoading ? "Cargando…" : "Buscar destino…"}
                />
              </div>
            )}

            {isSuperAdmin && isFlitProfile && (
              <p
                className="rounded-xl border px-3 py-2.5 text-xs"
                style={{ borderColor: "#00DBD5", background: "rgba(0,219,213,0.06)" }}
              >
                El perfil FLIT no pertenece a ninguna compañía ni organismo de tránsito: se creará
                con el rol de sistema <strong>Super Administrador</strong>.
              </p>
            )}

            <div>
              <label htmlFor="invite-name" className="mb-1 block text-xs font-semibold">
                Nombre completo *
              </label>
              <input
                id="invite-name"
                type="text"
                required
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder="Juan Pérez"
                className="w-full rounded-xl border bg-transparent px-3 py-2.5 text-sm outline-none focus:border-[#557EFF]"
              />
            </div>

            <div>
              <label htmlFor="invite-email" className="mb-1 block text-xs font-semibold">
                Correo electrónico *
              </label>
              <input
                id="invite-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="correo@empresa.com"
                className="w-full rounded-xl border bg-transparent px-3 py-2.5 text-sm outline-none focus:border-[#557EFF]"
              />
            </div>

            {/* Rol — solo los aplicables al perfil elegido. */}
            {!isFlitProfile &&
              (rolesBusy ? (
                <p className="text-xs opacity-60">Cargando roles…</p>
              ) : availableRoles.length > 0 ? (
                <fieldset>
                  <legend className="mb-1 block text-xs font-semibold">
                    Rol {isSuperAdmin ? "" : "*"}
                  </legend>
                  {/* Selección ÚNICA: un usuario tiene un solo rol y lo que define lo que puede
                      hacer son los permisos de ese rol. */}
                  <div className="grid grid-cols-1 gap-x-4 gap-y-1.5 rounded-xl border px-3 py-2.5 sm:grid-cols-2">
                    {availableRoles.map((r) => (
                      <label key={r.id} className="flex cursor-pointer items-center gap-2 text-xs">
                        <input
                          type="radio"
                          name="invite-role"
                          checked={selectedRoleIds[0] === r.id}
                          onChange={() => setSelectedRoleIds([r.id])}
                          className="h-3.5 w-3.5 accent-[#557EFF]"
                        />
                        <span>{r.name}</span>
                      </label>
                    ))}
                  </div>
                  <p
                    className="mt-1 text-[10px]"
                    style={{
                      color: rolesRequiredAndMissing ? "#FF4E00" : undefined,
                      opacity: rolesRequiredAndMissing ? 1 : 0.6,
                    }}
                  >
                    {rolesRequiredAndMissing
                      ? "Selecciona un rol."
                      : isSuperAdmin
                        ? "Un usuario tiene un solo rol. Sin selección se asigna el de administrador."
                        : "Un usuario tiene un solo rol; los permisos se ajustan sobre el rol."}
                  </p>
                </fieldset>
              ) : (
                <p
                  role="alert"
                  className="rounded-xl px-3 py-2 text-xs font-medium"
                  style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}
                >
                  No hay roles configurados para este perfil. Contacta al Super Admin.
                </p>
              ))}

            {error && (
              <p
                role="alert"
                className="rounded-xl px-3 py-2 text-xs font-medium"
                style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}
              >
                {error}
              </p>
            )}

            <OnboardingSteps />

            <button
              type="submit"
              disabled={
                status === "loading" || (needsTenant && !selectedTenantId) || rolesRequiredAndMissing
              }
              className="w-full rounded-xl py-2.5 text-sm font-semibold text-white transition disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {status === "loading" ? "Enviando…" : "Enviar Instrucciones"}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}

function resolveErrorMessage(code: string | undefined, status: number | undefined): string {
  switch (code) {
    case "NO_ROLES_SELECTED":
      return "Selecciona al menos un rol para el usuario invitado.";
    case "ROLE_TARGET_ENTITY_TYPE_MISMATCH":
      return "Alguno de los roles seleccionados no aplica al destino elegido.";
    case "FLIT_PROFILE_SINGLE_ROLE":
      return "El perfil FLIT no admite roles adicionales.";
    case "FLIT_PROFILE_HAS_NO_TENANT":
      return "El perfil FLIT no pertenece a una compañía ni a un organismo de tránsito.";
    case "TARGET_TENANT_REQUIRED":
      return "Debes seleccionar el destino del usuario.";
    default:
      break;
  }
  if (status === 409) return "Ya existe una invitación pendiente para este correo.";
  if (status === 404) return "Alguno de los roles seleccionados no existe.";
  if (status === 400) return "Revisa el destino y los roles seleccionados.";
  return "No se pudo enviar la invitación. Inténtalo de nuevo.";
}

function OnboardingSteps() {
  return (
    <div className="rounded-xl border bg-[rgba(0,219,213,0.06)] p-3">
      <p className="mb-2 text-[10px] font-semibold uppercase opacity-60">Onboarding</p>
      <div className="flex items-center gap-2 text-xs">
        {["Invitación enviada", "Activación", "Primer acceso"].map((step, i) => (
          <span key={step} className="flex items-center gap-1">
            <span
              className="grid h-5 w-5 place-items-center rounded-full text-[9px] font-bold"
              style={{
                background: i === 0 ? "#00DBD5" : "#DFE5ED",
                color: i === 0 ? "#fff" : "#162744",
              }}
            >
              {i + 1}
            </span>
            <span className={i === 0 ? "font-semibold" : "opacity-60"}>{step}</span>
          </span>
        ))}
      </div>
    </div>
  );
}

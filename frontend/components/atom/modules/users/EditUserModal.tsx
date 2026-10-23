"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, UserCog } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";
import { sanitizeName, validateReadableName } from "@/lib/validation/fieldRules";
import {
  EMAIL_ALREADY_ASSOCIATED_MESSAGE,
  emailConflictErrorCode,
  isEmailConflictCode,
} from "@/lib/users/emailConflict";
import type { TenantRole } from "@/lib/api/security";
import { countUserOverrides } from "@/lib/api/permission-overrides";
import {
  selectableRolesForProfile,
  USER_PROFILE_DESCRIPTION,
  type UserProfileKind,
} from "@/lib/users/profiles";
import { ProfileBadge } from "./ProfileRoleCell";

// HU #10621 — Modal "Editar usuario" del listado de la tabla de usuarios (módulo
// Compañía "Usuarios y Permisos" y pestaña "Usuarios" del hub OT). Formulario
// prellenado (AC1), foco inicial en el campo de nombre y mapeo de errores 409/404
// del backend sin perder lo escrito en el formulario (AC2/AC3). Sigue de cerca el
// patrón de EditCompanyDialog.tsx (rowVersion, conflicto de concurrencia).
export interface EditUserModalTarget {
  id: string;
  fullName: string;
  email: string;
  /** Concurrencia optimista (AC3): se reenvía tal cual se leyó al abrir el modal. */
  rowVersion: number;
}

export interface UpdateUserPayload {
  displayName?: string;
  email?: string;
  rowVersion: number;
}

export interface EditUserRoleSection {
  currentRoleName: string | null;
  currentRoleId: string | null;
  roles: TenantRole[];
  rolesLoading: boolean;
  onAssignRole: (roleId: string) => Promise<void>;
  /**
   * HU #13657 (FD2-5): tenant del usuario cuando quien edita es Super Admin (`?tenantId=` del conteo previo).
   * Los demás administradores usan el de su sesión.
   */
  tenantId?: string | null;
}

/** HU #13657: aviso previo al cambio de rol. `count = null` = no se pudo consultar cuántos permisos tiene. */
interface PendingRoleChange {
  roleId: string;
  count: number | null;
}

export function roleChangeWarning(count: number | null): string {
  if (count === null) {
    return "No pudimos verificar si este usuario tiene permisos individuales. Si los tiene, se eliminarán al cambiar su rol.";
  }
  return count === 1
    ? "Este usuario tiene 1 permiso individual que se eliminará al cambiar su rol."
    : `Este usuario tiene ${count} permisos individuales que se eliminarán al cambiar su rol.`;
}

export interface EditUserModalProps {
  user: EditUserModalTarget;
  onClose: () => void;
  onSaved: () => void;
  /** Persiste los cambios. Inyectado para reutilizar el modal entre Usuarios.tsx
   *  (updateUser, tenant de Compañía) y OtUsersSection.tsx (updateOtUser, con scope OT) —
   *  mismo patrón de inyección que EditCompanyDialog.onUpdate. */
  onUpdate: (userId: string, payload: UpdateUserPayload) => Promise<void>;
  /** Cambio de rol en el mismo modal (fuente única). Ausente = no mostrar sección (p. ej. OT). */
  roleSection?: EditUserRoleSection;
  /** Perfil del usuario que se edita. Se muestra siempre —también cuando no hay sección de
   *  rol— para que quede claro en qué relación está parado quien edita. */
  profile?: UserProfileKind;
}

const GENERIC_ERROR = "No se pudieron guardar los cambios. Inténtalo de nuevo.";

export function EditUserModal({
  user,
  onClose,
  onSaved,
  onUpdate,
  roleSection,
  profile,
}: EditUserModalProps) {
  const [displayName, setDisplayName] = useState(user.fullName);
  const [nameError, setNameError] = useState<string | null>(null);
  // Error a nivel de formulario (no asociado a un campo): correo en uso, cuenta eliminada
  // o conflicto de concurrencia (AC2/AC3).
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [roleBusy, setRoleBusy] = useState(false);
  const [roleError, setRoleError] = useState<string | null>(null);
  const [roleNotice, setRoleNotice] = useState<string | null>(null);
  const [pendingRole, setPendingRole] = useState<PendingRoleChange | null>(null);
  const firstFieldRef = useRef<HTMLInputElement>(null);
  const cancelRoleRef = useRef<HTMLButtonElement>(null);

  // El aviso de cambio de rol toma el foco (botón seguro: Cancelar) y lo devuelve al cerrarse.
  useEffect(() => {
    if (pendingRole) cancelRoleRef.current?.focus();
  }, [pendingRole]);

  // Foco inicial en el primer campo al montar (el modal solo se monta cuando hay
  // un usuario objetivo — mismo patrón condicional que EditCompanyDialog en su página).
  useEffect(() => {
    firstFieldRef.current?.focus();
  }, []);

  const handleClose = () => {
    if (busy || roleBusy) return;
    onClose();
  };

  // Roles ofrecibles: los del catálogo que corresponden al perfil del usuario. El catálogo
  // COMPANY incluye el rol SuperAdmin, que no puede darse a un Gestor ni a un funcionario de OT
  // por ningún motivo (el backend responde SUPERADMIN_ROLE_NOT_ASSIGNABLE); aquí ni se ofrece.
  const selectableRoles = roleSection
    ? selectableRolesForProfile(roleSection.roles, profile ?? "GESTOR")
    : [];
  // El rol vigente puede quedar fuera de la lista (rol de otro perfil, inactivo o filtrado):
  // se muestra igual como opción deshabilitada para que el select no aparezca en blanco.
  const currentRoleMissing =
    !!roleSection &&
    (!roleSection.currentRoleId || !selectableRoles.some((r) => r.id === roleSection.currentRoleId));

  async function handleRoleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    if (!roleSection) return;
    const roleId = e.target.value;
    if (!roleId || roleId === roleSection.currentRoleId) return;
    setRoleBusy(true);
    setRoleError(null);
    setRoleNotice(null);
    // HU #13657: al cambiar de rol el backend borra los permisos individuales del producto del rol nuevo y de los roles que
    // se cierran; se consulta cuántos son (por roleId, la misma regla) para avisar ANTES de aplicar. Sin ellos no hay aviso (AC2).
    const newRole = roleSection.roles.find((r) => r.id === roleId);
    let count: number | null = null;
    try {
      count = (await countUserOverrides(user.id, newRole?.productCode, roleSection.tenantId ?? undefined, roleId)).count;
    } catch {
      count = null;
    }
    if (count === null || count > 0) {
      setPendingRole({ roleId, count });
      setRoleBusy(false);
      return;
    }
    await applyRole(roleId);
  }

  async function applyRole(roleId: string) {
    if (!roleSection) return;
    setRoleBusy(true);
    setRoleError(null);
    try {
      await roleSection.onAssignRole(roleId);
      setRoleNotice("Rol actualizado.");
    } catch {
      setRoleError("No se pudo cambiar el rol. Inténtalo de nuevo.");
    } finally {
      setPendingRole(null);
      setRoleBusy(false);
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();

    const trimmedName = displayName.trim();

    if (!trimmedName) {
      setNameError("El nombre es obligatorio.");
      return;
    }
    const nameValidationError = validateReadableName(trimmedName, "El nombre");
    if (nameValidationError) {
      setNameError(nameValidationError);
      return;
    }

    setNameError(null);
    setFormError(null);
    setBusy(true);
    try {
      // El correo NO se envía: es el identificador de acceso del usuario y no se cambia desde
      // aquí (el input se muestra en solo lectura).
      await onUpdate(user.id, {
        displayName: trimmedName,
        rowVersion: user.rowVersion,
      });
      onSaved();
    } catch (error) {
      // No se pierde lo escrito: displayName/email no se resetean, solo se muestra el error.
      if (error instanceof ApiError) {
        const code = emailConflictErrorCode(error.body);
        // Mensaje unificado (HU #11550) — mismo texto para el único código
        // EMAIL_ALREADY_IN_USE (HU #11580) que en InviteUserModal / OtUsersSection.
        if (isEmailConflictCode(code)) {
          setFormError(EMAIL_ALREADY_ASSOCIATED_MESSAGE);
        } else if (code === "CONCURRENCY_CONFLICT" || error.status === 409) {
          setFormError(
            "Este usuario fue modificado por otra persona. Cierra el diálogo y vuelve a abrirlo.",
          );
        } else if (error.status === 404) {
          setFormError("El usuario ya no existe.");
        } else {
          setFormError(GENERIC_ERROR);
        }
      } else {
        setFormError(GENERIC_ERROR);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={handleClose}
      busy={busy || roleBusy}
      icon={UserCog}
      title="Editar usuario"
      description={user.email}
    >
      <form onSubmit={handleSubmit} className="space-y-3.5">
        {profile && (
          // Solo informativo: el perfil lo determina el tenant al que pertenece el usuario, no
          // se cambia desde aquí. Está para que quien edita sepa en qué relación está parado.
          <div
            className="flex items-start gap-2 rounded-xl border px-3 py-2"
            style={{ borderColor: "#DFE5ED", background: "rgba(223,229,237,0.18)" }}
          >
            <div className="min-w-0">
              <p className="text-[10px] font-semibold uppercase tracking-wide opacity-60">Perfil</p>
              <div className="mt-1">
                <ProfileBadge profile={profile} full />
              </div>
              <p className="mt-1 text-[10px] opacity-60">{USER_PROFILE_DESCRIPTION[profile]}</p>
            </div>
          </div>
        )}

        <Field label="Nombre completo" htmlFor="eu-name" error={nameError}>
          <input
            ref={firstFieldRef}
            id="eu-name"
            type="text"
            value={displayName}
            onChange={(e) => setDisplayName(sanitizeName(e.target.value))}
            maxLength={255}
            required
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-flit-brand focus:ring-2 focus:ring-flit-brand/20"
            style={{ borderColor: nameError ? "#FF4E00" : "#DFE5ED" }}
          />
        </Field>

        <Field label="Correo electrónico" htmlFor="eu-email">
          <input
            id="eu-email"
            type="email"
            readOnly
            value={user.email}
            className="w-full cursor-not-allowed rounded-xl border px-3 py-2 text-xs opacity-70 outline-none"
            style={{ borderColor: "#DFE5ED", background: "rgba(223,229,237,0.25)" }}
          />
          <p className="mt-1 text-[10px] opacity-60">
            El correo es la credencial de acceso del usuario y no se puede cambiar desde aquí.
          </p>
        </Field>

        {roleSection && (
          <Field label="Rol" htmlFor="eu-role" error={roleError}>
            <select
              id="eu-role"
              aria-label="Cambiar rol del usuario"
              value={currentRoleMissing ? "" : roleSection.currentRoleId ?? ""}
              onChange={handleRoleChange}
              disabled={roleBusy || pendingRole !== null || roleSection.rolesLoading || selectableRoles.length === 0}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-flit-brand focus:ring-2 focus:ring-flit-brand/20 disabled:opacity-60"
              style={{ borderColor: "#DFE5ED" }}
            >
              {roleSection.rolesLoading ? (
                <option value="">Cargando roles…</option>
              ) : selectableRoles.length === 0 ? (
                <option value="">No hay roles disponibles para este perfil</option>
              ) : (
                <>
                  {currentRoleMissing && (
                    <option value="" disabled>
                      {roleSection.currentRoleName ?? "Sin rol"}
                    </option>
                  )}
                  {selectableRoles.map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.name}
                    </option>
                  ))}
                </>
              )}
            </select>
            {pendingRole && (
              <div
                role="alertdialog"
                aria-labelledby="eu-role-warning"
                className="mt-2 space-y-2 rounded-lg px-3 py-2"
                style={{ background: "rgba(255,78,0,0.1)", color: "#FF4E00" }}
              >
                <p id="eu-role-warning" className="text-[11px] font-medium">
                  {roleChangeWarning(pendingRole.count)}
                </p>
                <p className="text-[10px] opacity-80">¿Cambiar el rol de todos modos?</p>
                <div className="flex justify-end gap-2">
                  <button
                    ref={cancelRoleRef}
                    type="button"
                    onClick={() => setPendingRole(null)}
                    disabled={roleBusy}
                    className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
                    style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
                  >
                    Mantener rol actual
                  </button>
                  <button
                    type="button"
                    onClick={() => void applyRole(pendingRole.roleId)}
                    disabled={roleBusy}
                    className="inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-60"
                    style={{ background: "#FF4E00" }}
                  >
                    {roleBusy && <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />}
                    Cambiar rol
                  </button>
                </div>
              </div>
            )}
            {roleNotice && (
              <p className="mt-1 text-[10px] font-medium" style={{ color: "var(--color-flit-tech)" }}>
                {roleNotice}
              </p>
            )}
            <p className="mt-1 text-[10px] opacity-60">
              El cambio de rol se aplica de inmediato; no requiere Guardar.
            </p>
          </Field>
        )}

        {formError && (
          <p
            role="alert"
            className="rounded-lg px-3 py-2 text-[11px] font-medium"
            style={{ background: "rgba(255,78,0,0.1)", color: "#FF4E00" }}
          >
            {formError}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={handleClose}
            disabled={busy || roleBusy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={busy || roleBusy}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,var(--color-flit-brand),var(--color-flit-tech))" }}
          >
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {busy ? "Guardando…" : "Guardar cambios"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function Field({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string | null;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {error && (
        <p className="mt-1 text-[10px] font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}

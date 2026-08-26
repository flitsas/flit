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
  const firstFieldRef = useRef<HTMLInputElement>(null);

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
    try {
      await roleSection.onAssignRole(roleId);
      setRoleNotice("Rol actualizado.");
    } catch {
      setRoleError("No se pudo cambiar el rol. Inténtalo de nuevo.");
    } finally {
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
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
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
              disabled={roleBusy || roleSection.rolesLoading || selectableRoles.length === 0}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:opacity-60"
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
            {roleNotice && (
              <p className="mt-1 text-[10px] font-medium" style={{ color: "#00DBD5" }}>
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
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
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

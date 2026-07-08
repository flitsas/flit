"use client";

import { useEffect, useRef, useState } from "react";
import { Loader2, UserCog } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";
import { sanitizeName, validateReadableName } from "@/lib/validation/fieldRules";

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

export interface EditUserModalProps {
  user: EditUserModalTarget;
  onClose: () => void;
  onSaved: () => void;
  /** Persiste los cambios. Inyectado para reutilizar el modal entre Usuarios.tsx
   *  (updateUser, tenant de Compañía) y OtUsersSection.tsx (updateOtUser, con scope OT) —
   *  mismo patrón de inyección que EditCompanyDialog.onUpdate. */
  onUpdate: (userId: string, payload: UpdateUserPayload) => Promise<void>;
}

const GENERIC_ERROR = "No se pudieron guardar los cambios. Inténtalo de nuevo.";

export function EditUserModal({ user, onClose, onSaved, onUpdate }: EditUserModalProps) {
  const [displayName, setDisplayName] = useState(user.fullName);
  const [email, setEmail] = useState(user.email);
  const [nameError, setNameError] = useState<string | null>(null);
  // Error a nivel de formulario (no asociado a un campo): correo en uso, cuenta eliminada
  // o conflicto de concurrencia (AC2/AC3).
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const firstFieldRef = useRef<HTMLInputElement>(null);

  // Foco inicial en el primer campo al montar (el modal solo se monta cuando hay
  // un usuario objetivo — mismo patrón condicional que EditCompanyDialog en su página).
  useEffect(() => {
    firstFieldRef.current?.focus();
  }, []);

  const handleClose = () => {
    if (busy) return;
    onClose();
  };

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();

    const trimmedName = displayName.trim();
    const trimmedEmail = email.trim();

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
      await onUpdate(user.id, {
        displayName: trimmedName,
        email: trimmedEmail,
        rowVersion: user.rowVersion,
      });
      onSaved();
    } catch (error) {
      // No se pierde lo escrito: displayName/email no se resetean, solo se muestra el error.
      if (error instanceof ApiError) {
        const code = (error.body as { code?: string } | undefined)?.code;
        if (code === "USER_ALREADY_EXISTS") {
          setFormError("Ese correo ya está en uso por otra cuenta.");
        } else if (code === "EMAIL_BELONGS_TO_DELETED_USER") {
          setFormError(
            "Ese correo pertenece a una cuenta eliminada. Contacta a un SuperAdmin para restaurarla.",
          );
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
      busy={busy}
      icon={UserCog}
      title="Editar usuario"
      description={user.email}
    >
      <form onSubmit={handleSubmit} className="space-y-3.5">
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
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            maxLength={255}
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
          />
        </Field>

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
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={busy}
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

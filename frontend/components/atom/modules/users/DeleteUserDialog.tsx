"use client";

import { useState } from "react";
import { Loader2, Trash2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";

// HU #10623 — Diálogo de confirmación "Eliminar usuario" del menú de acciones de la tabla
// (módulo Compañía "Usuarios y Permisos" y pestaña "Usuarios" del hub OT). AC1: deja explícito
// que la eliminación es reversible y que SOLO un SuperAdmin puede restaurar al usuario. Mapea
// SELF_DELETE/LAST_ACTIVE_ADMIN/CONCURRENCY_CONFLICT como defensa en profundidad (el botón no
// debería mostrarse sobre la propia fila — AC2 — ni sobre el último administrador activo).
// Mismo patrón de inyección de `onDelete` que EditUserModal.onUpdate: reutilizable entre
// Usuarios.tsx (deleteUser) y OtUsersSection.tsx (deleteOtUser con scope OT).
export interface DeleteUserDialogTarget {
  id: string;
  fullName: string;
  email: string;
  /** Concurrencia optimista: se reenvía tal cual se leyó al abrir el diálogo. */
  rowVersion: number;
}

export interface DeleteUserDialogProps {
  user: DeleteUserDialogTarget;
  onClose: () => void;
  onDeleted: () => void;
  onDelete: (userId: string, rowVersion: number) => Promise<void>;
}

const GENERIC_ERROR = "No se pudo eliminar al usuario. Inténtalo de nuevo.";

export function DeleteUserDialog({ user, onClose, onDeleted, onDelete }: DeleteUserDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleClose() {
    if (busy) return;
    onClose();
  }

  async function handleConfirm() {
    setBusy(true);
    setError(null);
    try {
      await onDelete(user.id, user.rowVersion);
      onDeleted();
    } catch (err) {
      if (err instanceof ApiError) {
        // SecurityEndpoints (Compañía) serializa el error como { code, message }; AdminOtEndpoints
        // (OT) lo hace como { error, message } — se aceptan ambas formas (ver admin-ot-security.ts).
        const body = err.body as { code?: string; error?: string } | undefined;
        const errorCode = body?.code ?? body?.error;
        if (errorCode === "SELF_DELETE") {
          setError("No puedes eliminarte a ti mismo.");
        } else if (errorCode === "LAST_ACTIVE_ADMIN") {
          setError("No es posible eliminar al último administrador activo del tenant o del sistema.");
        } else if (errorCode === "CONCURRENCY_CONFLICT" || err.status === 409) {
          setError("Este usuario fue modificado por otra persona. Cierra el diálogo y vuelve a abrirlo.");
        } else if (err.status === 404) {
          setError("El usuario ya no existe.");
        } else {
          setError(GENERIC_ERROR);
        }
      } else {
        setError(GENERIC_ERROR);
      }
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={handleClose}
      busy={busy}
      icon={Trash2}
      iconBg="#FF4E00"
      title="Eliminar usuario"
      description={user.email}
    >
      <div className="space-y-3.5">
        <p className="text-xs">
          ¿Seguro que deseas eliminar a <strong>{user.fullName}</strong>? Perderá acceso a la
          plataforma de inmediato.
        </p>
        <p
          className="rounded-lg px-3 py-2 text-[11px] font-medium"
          style={{ background: "rgba(85,126,255,0.08)", color: "#557EFF" }}
        >
          Esta acción es reversible: solo un <strong>Super Admin</strong> puede restaurarlo más
          adelante desde la vista de usuarios eliminados.
        </p>

        {error && (
          <p
            role="alert"
            className="rounded-lg px-3 py-2 text-[11px] font-medium"
            style={{ background: "rgba(255,78,0,0.1)", color: "#FF4E00" }}
          >
            {error}
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
            type="button"
            onClick={handleConfirm}
            disabled={busy}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "#FF4E00" }}
          >
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {busy ? "Eliminando…" : "Eliminar usuario"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

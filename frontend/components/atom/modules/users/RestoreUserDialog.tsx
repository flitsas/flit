"use client";

import { useState } from "react";
import { Loader2, RotateCcw } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";

// HU #10623 (AC3) — Diálogo de confirmación "Restaurar usuario" para la vista de "Eliminados",
// exclusiva de SuperAdmin. Un clic de confirmación deshace el soft-delete: el usuario recupera
// exactamente el mismo estado (roles y suspensiones) que tenía al momento de eliminarse — el
// backend (`POST /superadmin/users/{userId}/restore`) no toca esas tablas.
export interface RestoreUserDialogTarget {
  id: string;
  fullName: string;
  email: string;
}

export interface RestoreUserDialogProps {
  user: RestoreUserDialogTarget;
  onClose: () => void;
  onRestored: () => void;
  onRestore: (userId: string) => Promise<void>;
}

const GENERIC_ERROR = "No se pudo restaurar al usuario. Inténtalo de nuevo.";

export function RestoreUserDialog({ user, onClose, onRestored, onRestore }: RestoreUserDialogProps) {
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
      await onRestore(user.id);
      onRestored();
    } catch (err) {
      if (err instanceof ApiError) {
        const body = err.body as { code?: string; error?: string } | undefined;
        const errorCode = body?.code ?? body?.error;
        if (errorCode === "USER_NOT_DELETED") {
          setError("Este usuario ya no está eliminado.");
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
      icon={RotateCcw}
      iconBg="#00DBD5"
      title="Restaurar usuario"
      description={user.email}
    >
      <div className="space-y-3.5">
        <p className="text-xs">
          ¿Restaurar a <strong>{user.fullName}</strong>? Recuperará exactamente el mismo rol y
          estado que tenía al momento de eliminarse.
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
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {busy ? "Restaurando…" : "Restaurar usuario"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

"use client";

import { useState } from "react";
import { Loader2, MailX } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";

// HU #10628 — Diálogo de confirmación "Cancelar invitación" del menú de acciones de la tabla
// (módulo Compañía "Usuarios y Permisos" y pestaña "Usuarios" del hub OT). AC2: acción DISTINTA
// de "Eliminar usuario" (DeleteUserDialog) — nunca coexisten sobre la misma fila (una fila
// "Pendiente" todavía no tiene una cuenta creada). Mismo patrón visual que DeleteUserDialog: un
// diálogo de confirmación sobre Modal.tsx, con `onConfirm` inyectado por el padre
// (Usuarios.tsx → cancelInvitation, OtUsersSection.tsx → cancelOtInvitation con scope OT).
export interface CancelInvitationDialogTarget {
  /** El `id` de la fila "pending" — ya es el invitationId, sin campo nuevo (ver ResendInvitationButton). */
  id: string;
  fullName: string;
  email: string;
}

export interface CancelInvitationDialogProps {
  invitation: CancelInvitationDialogTarget;
  onClose: () => void;
  /** Cancelación exitosa (204): el padre cierra el diálogo y refresca el listado. */
  onCancelled: () => void;
  /** AC3 — la invitación ya no existe (404) o ya no está pendiente (409): condición de carrera
   *  (fue aceptada o cancelada por otra persona justo antes de confirmar). El padre debe
   *  refrescar el listado en segundo plano para que la fila fantasma desaparezca, SIN cerrar el
   *  diálogo todavía — el mensaje de error sigue visible hasta que el usuario lo cierra. */
  onStale: () => void;
  onCancel: (invitationId: string) => Promise<void>;
}

const GENERIC_ERROR = "No se pudo cancelar la invitación. Inténtalo de nuevo.";

export function CancelInvitationDialog({
  invitation,
  onClose,
  onCancelled,
  onStale,
  onCancel,
}: CancelInvitationDialogProps) {
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
      await onCancel(invitation.id);
      onCancelled();
    } catch (err) {
      if (err instanceof ApiError) {
        // SecurityEndpoints (Compañía) serializa el error como { code, message }; AdminOtEndpoints
        // (OT) lo hace como { error, message } — se aceptan ambas formas (ver admin-ot-security.ts).
        const body = err.body as { code?: string; error?: string } | undefined;
        const errorCode = body?.code ?? body?.error;
        if (err.status === 404 || errorCode === "INVITATION_NOT_FOUND") {
          // AC3 — condición de carrera: ya no existe (aceptada o cancelada por otra persona).
          setError(
            "Esta invitación ya no existe: alguien más la aceptó o canceló justo antes de tu confirmación. La lista se ha actualizado.",
          );
          onStale();
        } else if (err.status === 409 || errorCode === "INVITATION_NOT_PENDING") {
          // AC3 — condición de carrera: sigue existiendo, pero ya no está pendiente.
          setError(
            "Esta invitación ya no está pendiente: fue aceptada o cancelada por otra persona. La lista se ha actualizado.",
          );
          onStale();
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
      icon={MailX}
      iconBg="#FF4E00"
      title="Cancelar invitación"
      description={invitation.email}
    >
      <div className="space-y-3.5">
        <p className="text-xs">
          ¿Seguro que deseas cancelar la invitación de <strong>{invitation.fullName}</strong>? El
          enlace de activación dejará de funcionar y podrás invitar de nuevo a este correo más
          adelante.
        </p>
        <p
          className="rounded-lg px-3 py-2 text-[11px] font-medium"
          style={{ background: "rgba(85,126,255,0.08)", color: "#557EFF" }}
        >
          Esta acción es distinta de <strong>Eliminar usuario</strong>: todavía no existe ninguna
          cuenta creada, solo se anula la invitación pendiente.
        </p>

        {error && (
          <p
            role="alert"
            aria-live="assertive"
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
            {error ? "Cerrar" : "Volver"}
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={busy}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "#FF4E00" }}
          >
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {busy ? "Cancelando…" : "Cancelar invitación"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

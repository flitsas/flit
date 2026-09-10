"use client";

import { useState } from "react";
import { KeyRound, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiError } from "@/lib/api/types";
import { adminResetPassword } from "@/lib/api/auth";
import { isPasswordReusedError } from "@/lib/auth/passwordReusedError";

export interface ResetPasswordDialogTarget {
  fullName: string;
  email: string;
}

export interface ResetPasswordDialogProps {
  user: ResetPasswordDialogTarget;
  onClose: () => void;
  onDone: () => void;
}

const GENERIC_ERROR = "No se pudo restablecer la contraseña. Inténtalo de nuevo.";

// Aquí la contraseña la genera el sistema, no el admin: si colisiona con la
// vigente el backend la regenera solo (hasta 5 intentos) y completa el reset sin
// error. Que este 409 llegue igual significa que el generador está roto — una
// condición sobre la que el admin no puede actuar. Se le da un texto honesto de
// error del sistema (en vez de caer en el genérico, que sonaría a "reintenta y
// probablemente funcione") para no sugerir una reutilización que no ocurrió y
// para no insinuar que el admin hizo algo mal.
const SYSTEM_PASSWORD_GENERATION_ERROR =
  "No se pudo generar una contraseña temporal válida. Este es un error del sistema, no de tu solicitud — contacta a soporte si persiste.";

/**
 * Confirmación de reset administrativo (HU-B auth-parity / HU #10170/#10174).
 * Éxito: el usuario recibirá una temporal y deberá cambiarla en el próximo login.
 */
export function ResetPasswordDialog({ user, onClose, onDone }: ResetPasswordDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  function handleClose() {
    if (busy) return;
    onClose();
  }

  async function handleConfirm() {
    setError(null);
    setBusy(true);
    try {
      await adminResetPassword(user.email);
      setSuccess(true);
      onDone();
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 403) {
          setError("Acceso restringido: no tiene ámbito sobre este usuario.");
        } else if (err.status === 404) {
          setError("El usuario no existe o no está activo.");
        } else if (isPasswordReusedError(err.status, err.body)) {
          setError(SYSTEM_PASSWORD_GENERATION_ERROR);
        } else {
          setError(err.message || GENERIC_ERROR);
        }
      } else {
        setError(GENERIC_ERROR);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={handleClose} title="Restablecer contraseña" size="sm">
      <div className="space-y-4 p-1">
        {success ? (
          <p className="text-sm opacity-80" role="status">
            Se restableció la contraseña de <strong>{user.fullName}</strong>. El usuario recibió una
            temporal por correo y deberá definir una nueva en su próximo inicio de sesión.
          </p>
        ) : (
          <>
            <p className="text-sm opacity-80">
              ¿Restablecer la contraseña de <strong>{user.fullName}</strong> ({user.email})? Se
              generará una temporal, se enviará por correo y deberá cambiarla al iniciar sesión.
            </p>
            {error && (
              <p className="text-sm" style={{ color: "#FF4E00" }} role="alert">
                {error}
              </p>
            )}
          </>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            onClick={handleClose}
            disabled={busy}
            className="rounded-lg px-3 py-2 text-sm font-medium opacity-70 hover:opacity-100"
          >
            {success ? "Cerrar" : "Cancelar"}
          </button>
          {!success && (
            <button
              type="button"
              onClick={handleConfirm}
              disabled={busy}
              className="inline-flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-semibold text-white"
              style={{ background: "#557EFF" }}
            >
              {busy ? (
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
              ) : (
                <KeyRound className="h-4 w-4" aria-hidden />
              )}
              Confirmar reset
            </button>
          )}
        </div>
      </div>
    </Modal>
  );
}

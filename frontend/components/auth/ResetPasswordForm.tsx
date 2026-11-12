"use client";

// Restablecimiento por enlace (HU #10173, AC1/AC2). Valida la política en cliente,
// confirma éxito y ofrece ir a login. Token ausente/expirado/usado → error claro
// sin exponer datos sensibles.
import Link from "next/link";
import { useState } from "react";
import { resetPassword } from "@/lib/api/auth";
import { getToken } from "@/lib/api/client";
import { clearToken } from "@/lib/auth/session";
import { isPasswordCompliant, PASSWORD_POLICY_HINT } from "@/lib/auth/password-policy";
import { isPasswordReusedError, PASSWORD_REUSED_MESSAGE } from "@/lib/auth/passwordReusedError";
import { Lock } from "lucide-react";
import { INGRESO_ICONO_CLS } from "@flit/ui/Ingreso";
import {
  AUTH_ERROR_CLASS,
  AUTH_HINT_CLASS,
  AUTH_INPUT_CLASS,
  AUTH_LABEL_CLASS,
  AUTH_LINK_BUTTON_CLASS,
  AUTH_NOTE_CLASS,
  AUTH_STATUS_CLASS,
  AUTH_SUBMIT_CLASS,
} from "./AuthCard";

export function ResetPasswordForm({ token }: { token: string | null }) {
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [done, setDone] = useState(false);
  const [hadPriorSession, setHadPriorSession] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (!token) {
    return (
      <p role="alert" className={AUTH_ERROR_CLASS}>
        El enlace de recuperación es inválido o expiró. Solicita uno nuevo.
      </p>
    );
  }

  if (done) {
    return (
      <div role="status" className="grid gap-4">
        <p className={AUTH_STATUS_CLASS}>Tu contraseña fue actualizada correctamente.</p>
        {hadPriorSession && (
          <p className={AUTH_NOTE_CLASS}>
            Por tu seguridad, cerramos la sesión que estaba activa en este navegador. Inicia sesión con tu nueva cuenta.
          </p>
        )}
        <Link
          href="/login"
          className={AUTH_LINK_BUTTON_CLASS}
        >
          Ir a iniciar sesión
        </Link>
      </div>
    );
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!isPasswordCompliant(password)) {
      setError(PASSWORD_POLICY_HINT);
      return;
    }
    if (password !== confirm) {
      setError("Las contraseñas no coinciden.");
      return;
    }
    setLoading(true);
    try {
      await resetPassword(token as string, password);
      setHadPriorSession(Boolean(getToken()));
      clearToken();
      setDone(true);
    } catch (err) {
      // El token de recuperación sigue siendo válido tras este rechazo (el backend lo
      // reordenó a propósito): no limpiar el token, no marcar `done` ni redirigir — el
      // usuario debe poder reintentar con otra contraseña en esta misma pantalla.
      const status = (err as { status?: number }).status;
      const body = (err as { body?: unknown }).body;
      if (isPasswordReusedError(status, body)) {
        setError(PASSWORD_REUSED_MESSAGE);
      } else if (status === 400) {
        setError("El enlace de recuperación es inválido o expiró. Solicita uno nuevo.");
      } else {
        setError("No se pudo restablecer la contraseña. Inténtalo de nuevo.");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="grid gap-4" aria-label="Restablecer contraseña" noValidate>
      <div>
        <label htmlFor="rp-password" className={AUTH_LABEL_CLASS}>
          Nueva contraseña
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="rp-password"
            type="password"
            autoComplete="new-password"
            className={AUTH_INPUT_CLASS}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </div>
        <p className={AUTH_HINT_CLASS}>{PASSWORD_POLICY_HINT}</p>
      </div>
      <div>
        <label htmlFor="rp-confirm" className={AUTH_LABEL_CLASS}>
          Confirmar contraseña
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="rp-confirm"
            type="password"
            autoComplete="new-password"
            className={AUTH_INPUT_CLASS}
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </div>
      </div>
      {error && (
        <p role="alert" className={AUTH_ERROR_CLASS}>
          {error}
        </p>
      )}
      <button
        type="submit"
        disabled={loading}
        className={AUTH_SUBMIT_CLASS}
      >
        {loading ? "Guardando…" : "Restablecer contraseña"}
      </button>
    </form>
  );
}

"use client";

import Link from "next/link";
import { useState } from "react";
import { activateAccount } from "@/lib/api/auth";
import { getToken } from "@/lib/api/client";
import { ApiError } from "@/lib/api/types";
import { clearToken } from "@/lib/auth/session";
import { isPasswordCompliant, PASSWORD_POLICY_HINT } from "@/lib/auth/password-policy";
import {
  EMAIL_ALREADY_ASSOCIATED_MESSAGE,
  emailConflictErrorCode,
  isEmailConflictCode,
} from "@/lib/users/emailConflict";
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

export function ActivateAccountForm({ token }: { token: string | null }) {
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [done, setDone] = useState(false);
  const [hadPriorSession, setHadPriorSession] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (!token) {
    return (
      <p role="alert" className={AUTH_ERROR_CLASS}>
        El enlace de activación es inválido. Solicita una nueva invitación.
      </p>
    );
  }

  if (done) {
    return (
      <div role="status" className="grid gap-4">
        <p className={AUTH_STATUS_CLASS}>Tu cuenta fue activada correctamente.</p>
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

    // AC2 — validación inline sin llamar al servidor
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
      await activateAccount(token as string, password);
      setHadPriorSession(Boolean(getToken()));
      clearToken();
      setDone(true);
    } catch (err) {
      const apiErr = err as ApiError;
      setError(
        // HU #13811/#13812 — el correo ya lo usa otra cuenta (otra compañía lo activó antes): 409.
        isEmailConflictCode(emailConflictErrorCode(apiErr.body))
          ? EMAIL_ALREADY_ASSOCIATED_MESSAGE
          : apiErr.status === 400
            ? "El enlace de activación es inválido o ya fue utilizado."
            : "No se pudo activar la cuenta. Inténtalo de nuevo.",
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="grid gap-4" aria-label="Activar cuenta" noValidate>
      <div>
        <label htmlFor="ac-password" className={AUTH_LABEL_CLASS}>
          Nueva contraseña
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="ac-password"
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
        <label htmlFor="ac-confirm" className={AUTH_LABEL_CLASS}>
          Confirmar contraseña
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="ac-confirm"
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
        {loading ? "Activando…" : "Activar mi cuenta"}
      </button>
    </form>
  );
}

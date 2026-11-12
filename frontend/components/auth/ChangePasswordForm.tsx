"use client";

// Cambio voluntario desde el perfil (HU #10173, RF24). Valida la política en
// cliente y delega en PUT /auth/change-password. La complejidad se valida antes
// de enviar, por lo que un 400 del backend se atribuye a la contraseña actual.
import { useState } from "react";
import { changePassword } from "@/lib/api/auth";
import { isPasswordCompliant, PASSWORD_POLICY_HINT } from "@/lib/auth/password-policy";
import { isPasswordReusedError, PASSWORD_REUSED_MESSAGE } from "@/lib/auth/passwordReusedError";
import { Lock } from "lucide-react";
import { INGRESO_ICONO_CLS } from "@flit/ui/Ingreso";
import {
  AUTH_ERROR_CLASS,
  AUTH_HINT_CLASS,
  AUTH_INPUT_CLASS,
  AUTH_LABEL_CLASS,
  AUTH_STATUS_CLASS,
  AUTH_SUBMIT_CLASS,
} from "./AuthCard";

export function ChangePasswordForm({ onSuccess }: { onSuccess?: () => void }) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!current) {
      setError("Ingresa tu contraseña actual.");
      return;
    }
    if (!isPasswordCompliant(next)) {
      setError(PASSWORD_POLICY_HINT);
      return;
    }
    if (next !== confirm) {
      setError("Las contraseñas no coinciden.");
      return;
    }
    setLoading(true);
    try {
      await changePassword(current, next);
      setDone(true);
      onSuccess?.();
    } catch (err) {
      const status = (err as { status?: number }).status;
      const body = (err as { body?: unknown }).body;
      if (isPasswordReusedError(status, body)) {
        setError(PASSWORD_REUSED_MESSAGE);
      } else if (status === 400) {
        setError("La contraseña actual es incorrecta.");
      } else {
        setError("No se pudo cambiar la contraseña. Inténtalo de nuevo.");
      }
    } finally {
      setLoading(false);
    }
  }

  if (done) {
    return (
      <p role="status" className={AUTH_STATUS_CLASS}>
        Tu contraseña fue actualizada correctamente.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="grid gap-4" aria-label="Cambiar contraseña" noValidate>
      <div>
        <label htmlFor="cp-current" className={AUTH_LABEL_CLASS}>
          Contraseña actual
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="cp-current"
            type="password"
            autoComplete="current-password"
            className={AUTH_INPUT_CLASS}
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
          />
        </div>
      </div>
      <div>
        <label htmlFor="cp-next" className={AUTH_LABEL_CLASS}>
          Nueva contraseña
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="cp-next"
            type="password"
            autoComplete="new-password"
            className={AUTH_INPUT_CLASS}
            value={next}
            onChange={(e) => setNext(e.target.value)}
          />
        </div>
        <p className={AUTH_HINT_CLASS}>{PASSWORD_POLICY_HINT}</p>
      </div>
      <div>
        <label htmlFor="cp-confirm" className={AUTH_LABEL_CLASS}>
          Confirmar nueva contraseña
        </label>
        <div className="relative">
          <Lock className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="cp-confirm"
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
        {loading ? "Guardando…" : "Cambiar contraseña"}
      </button>
    </form>
  );
}

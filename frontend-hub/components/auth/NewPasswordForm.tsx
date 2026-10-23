"use client";

import Link from "next/link";
import { useState } from "react";
import { postJson } from "@/lib/api";
import { isPasswordCompliant, PASSWORD_POLICY_HINT } from "@/lib/password-policy";
import { isPasswordReusedError, PASSWORD_REUSED_MESSAGE } from "@/lib/password-reused";
import { INPUT_CLASS, SUBMIT_CLASS } from "./AuthCard";

/**
 * Fijar una contraseña con el token de un enlace: recuperación (HU #10173) o activación de invitación. Mismas reglas
 * y textos que Trámites (ResetPasswordForm y ActivateAccountForm). El token de recuperación sigue sirviendo tras un
 * 409 PASSWORD_REUSED, así que en ese caso se puede reintentar en la misma pantalla.
 */
export type NewPasswordMode = "reset" | "activate";

const TEXTS: Record<NewPasswordMode, { invalid: string; done: string; submit: string; busy: string; label: string }> = {
  reset: {
    invalid: "El enlace de recuperación es inválido o expiró. Solicita uno nuevo.",
    done: "Tu contraseña fue actualizada correctamente.",
    submit: "Restablecer contraseña",
    busy: "Guardando…",
    label: "Restablecer contraseña",
  },
  activate: {
    invalid: "El enlace de activación es inválido o ya fue utilizado.",
    done: "Tu cuenta fue activada correctamente.",
    submit: "Activar mi cuenta",
    busy: "Activando…",
    label: "Activar cuenta",
  },
};

export function NewPasswordForm({ mode, token }: { mode: NewPasswordMode; token: string | null }) {
  const texts = TEXTS[mode];
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (!token) {
    return <p role="alert" className="text-sm text-flit-alert">{mode === "reset" ? texts.invalid : "El enlace de activación es inválido. Solicita una nueva invitación."}</p>;
  }

  if (done) {
    return (
      <div role="status" className="space-y-4">
        <p className="text-sm text-slate-700">{texts.done}</p>
        <Link href="/login" className="inline-block rounded-xl bg-flit-brand px-4 py-2.5 text-sm font-semibold text-white">
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
      const result = mode === "reset"
        ? await postJson("/api/v1/auth/reset-password", { token, newPassword: password })
        : await postJson("/api/v1/auth/activate", { token, password });
      if (result.ok) {
        setDone(true);
      } else if (mode === "reset" && isPasswordReusedError(result.status, result.body)) {
        setError(PASSWORD_REUSED_MESSAGE);
      } else if (result.status === 400) {
        setError(texts.invalid);
      } else {
        setError(mode === "reset" ? "No se pudo restablecer la contraseña. Inténtalo de nuevo." : "No se pudo activar la cuenta. Inténtalo de nuevo.");
      }
    } catch {
      setError("No fue posible completar la operación. Inténtalo de nuevo.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label={texts.label} noValidate>
      <div>
        <label htmlFor="np-password" className="mb-1 block text-sm font-medium text-flit-primary">Nueva contraseña</label>
        <input id="np-password" type="password" autoComplete="new-password" className={INPUT_CLASS} value={password} onChange={(e) => setPassword(e.target.value)} />
        <p className="mt-1 text-xs text-slate-500">{PASSWORD_POLICY_HINT}</p>
      </div>
      <div>
        <label htmlFor="np-confirm" className="mb-1 block text-sm font-medium text-flit-primary">Confirmar contraseña</label>
        <input id="np-confirm" type="password" autoComplete="new-password" className={INPUT_CLASS} value={confirm} onChange={(e) => setConfirm(e.target.value)} />
      </div>
      {error && <p role="alert" className="text-sm text-flit-alert">{error}</p>}
      <button type="submit" disabled={loading} className={SUBMIT_CLASS}>
        {loading ? texts.busy : texts.submit}
      </button>
    </form>
  );
}

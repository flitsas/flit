"use client";

// Cambio voluntario desde el perfil (HU #10173, RF24). Valida la política en
// cliente y delega en PUT /auth/change-password. La complejidad se valida antes
// de enviar, por lo que un 400 del backend se atribuye a la contraseña actual.
import { useState } from "react";
import { changePassword } from "@/lib/api/auth";
import { isPasswordCompliant, PASSWORD_POLICY_HINT } from "@/lib/auth/password-policy";
import { isPasswordReusedError, PASSWORD_REUSED_MESSAGE } from "@/lib/auth/passwordReusedError";

const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

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
      <p role="status" className="text-sm text-slate-700">
        Tu contraseña fue actualizada correctamente.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label="Cambiar contraseña" noValidate>
      <div>
        <label htmlFor="cp-current" className="block text-sm font-medium text-[#162744] mb-1">
          Contraseña actual
        </label>
        <input
          id="cp-current"
          type="password"
          autoComplete="current-password"
          className={INPUT_CLASS}
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
        />
      </div>
      <div>
        <label htmlFor="cp-next" className="block text-sm font-medium text-[#162744] mb-1">
          Nueva contraseña
        </label>
        <input
          id="cp-next"
          type="password"
          autoComplete="new-password"
          className={INPUT_CLASS}
          value={next}
          onChange={(e) => setNext(e.target.value)}
        />
        <p className="mt-1 text-xs text-slate-500">{PASSWORD_POLICY_HINT}</p>
      </div>
      <div>
        <label htmlFor="cp-confirm" className="block text-sm font-medium text-[#162744] mb-1">
          Confirmar nueva contraseña
        </label>
        <input
          id="cp-confirm"
          type="password"
          autoComplete="new-password"
          className={INPUT_CLASS}
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
        />
      </div>
      {error && (
        <p role="alert" className="text-sm text-[#ff4e00]">
          {error}
        </p>
      )}
      <button
        type="submit"
        disabled={loading}
        className="w-full rounded-xl py-3 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60"
        style={{ background: "#557eff" }}
      >
        {loading ? "Guardando…" : "Cambiar contraseña"}
      </button>
    </form>
  );
}

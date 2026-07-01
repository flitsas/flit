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

const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

export function ResetPasswordForm({ token }: { token: string | null }) {
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [done, setDone] = useState(false);
  const [hadPriorSession, setHadPriorSession] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (!token) {
    return (
      <p role="alert" className="text-sm text-[#ff4e00]">
        El enlace de recuperación es inválido o expiró. Solicita uno nuevo.
      </p>
    );
  }

  if (done) {
    return (
      <div role="status" className="space-y-4">
        <p className="text-sm text-slate-700">Tu contraseña fue actualizada correctamente.</p>
        {hadPriorSession && (
          <p className="text-xs text-slate-500">
            Por tu seguridad, cerramos la sesión que estaba activa en este navegador. Inicia sesión con tu nueva cuenta.
          </p>
        )}
        <Link
          href="/login"
          className="inline-block rounded-xl px-4 py-2.5 text-sm font-semibold text-white"
          style={{ background: "#557eff" }}
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
      const status = (err as { status?: number }).status;
      setError(
        status === 400
          ? "El enlace de recuperación es inválido o expiró. Solicita uno nuevo."
          : "No se pudo restablecer la contraseña. Inténtalo de nuevo.",
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label="Restablecer contraseña" noValidate>
      <div>
        <label htmlFor="rp-password" className="block text-sm font-medium text-[#162744] mb-1">
          Nueva contraseña
        </label>
        <input
          id="rp-password"
          type="password"
          autoComplete="new-password"
          className={INPUT_CLASS}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <p className="mt-1 text-xs text-slate-500">{PASSWORD_POLICY_HINT}</p>
      </div>
      <div>
        <label htmlFor="rp-confirm" className="block text-sm font-medium text-[#162744] mb-1">
          Confirmar contraseña
        </label>
        <input
          id="rp-confirm"
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
        {loading ? "Guardando…" : "Restablecer contraseña"}
      </button>
    </form>
  );
}

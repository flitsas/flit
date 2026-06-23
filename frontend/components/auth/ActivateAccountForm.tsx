"use client";

import Link from "next/link";
import { useState } from "react";
import { activateAccount } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/types";
import { isPasswordCompliant, PASSWORD_POLICY_HINT } from "@/lib/auth/password-policy";

const INPUT_CLASS =
  "w-full bg-white border border-[#DFE5ED] rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

export function ActivateAccountForm({ token }: { token: string | null }) {
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (!token) {
    return (
      <p role="alert" className="text-sm text-[#ff4e00]">
        El enlace de activación es inválido. Solicita una nueva invitación.
      </p>
    );
  }

  if (done) {
    return (
      <div role="status" className="space-y-4">
        <p className="text-sm" style={{ color: "#162744" }}>Tu cuenta fue activada correctamente.</p>
        <Link
          href="/"
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
      setDone(true);
    } catch (err) {
      const apiErr = err as ApiError;
      setError(
        apiErr.status === 400
          ? "El enlace de activación es inválido o ya fue utilizado."
          : "No se pudo activar la cuenta. Inténtalo de nuevo.",
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label="Activar cuenta" noValidate>
      <div>
        <label htmlFor="ac-password" className="block text-sm font-medium text-[#162744] mb-1">
          Nueva contraseña
        </label>
        <input
          id="ac-password"
          type="password"
          autoComplete="new-password"
          className={INPUT_CLASS}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <p className="mt-1 text-xs opacity-50" style={{ color: "#162744" }}>{PASSWORD_POLICY_HINT}</p>
      </div>
      <div>
        <label htmlFor="ac-confirm" className="block text-sm font-medium text-[#162744] mb-1">
          Confirmar contraseña
        </label>
        <input
          id="ac-confirm"
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
        {loading ? "Activando…" : "Activar mi cuenta"}
      </button>
    </form>
  );
}

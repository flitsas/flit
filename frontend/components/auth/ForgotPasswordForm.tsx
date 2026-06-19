"use client";

// Solicitud de recuperación (HU #10173). Siempre muestra confirmación genérica
// (anti-enumeración), igual que el backend responde 202 genérico.
import { useState } from "react";
import { forgotPassword } from "@/lib/api/auth";

const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

export function ForgotPasswordForm() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    if (!email.trim()) {
      setError("Ingresa tu correo.");
      return;
    }
    setLoading(true);
    try {
      await forgotPassword(email.trim());
    } catch {
      // Respuesta genérica intencional: no revelar fallos.
    } finally {
      setLoading(false);
      setSent(true);
    }
  }

  if (sent) {
    return (
      <p role="status" className="text-sm text-slate-700">
        Si el correo está registrado, enviaremos instrucciones de recuperación.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label="Recuperar contraseña" noValidate>
      <div>
        <label htmlFor="fp-email" className="block text-sm font-medium text-[#162744] mb-1">
          Correo electrónico
        </label>
        <input
          id="fp-email"
          type="email"
          autoComplete="username"
          className={INPUT_CLASS}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
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
        {loading ? "Enviando…" : "Enviar instrucciones"}
      </button>
    </form>
  );
}

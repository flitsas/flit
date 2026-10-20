"use client";

import { useState } from "react";
import { postJson } from "@/lib/api";
import { INPUT_CLASS, SUBMIT_CLASS } from "./AuthCard";

// Solicitud de recuperación (HU #10173) en el hub. Confirmación genérica siempre (anti-enumeración), igual que el
// backend, que responde 202 exista o no el correo.
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
      await postJson("/api/v1/auth/forgot-password", { email: email.trim() });
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
        <label htmlFor="fp-email" className="mb-1 block text-sm font-medium text-flit-primary">Correo electrónico</label>
        <input id="fp-email" type="email" autoComplete="username" className={INPUT_CLASS} value={email} onChange={(e) => setEmail(e.target.value)} />
      </div>
      {error && <p role="alert" className="text-sm text-flit-alert">{error}</p>}
      <button type="submit" disabled={loading} className={SUBMIT_CLASS}>
        {loading ? "Enviando…" : "Enviar instrucciones"}
      </button>
    </form>
  );
}

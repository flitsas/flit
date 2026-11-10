"use client";

// Solicitud de recuperación (HU #10173). Siempre muestra confirmación genérica
// (anti-enumeración), igual que el backend responde 202 genérico.
import { useState } from "react";
import { forgotPassword } from "@/lib/api/auth";
import { Mail } from "lucide-react";
import { INGRESO_ICONO_CLS } from "@flit/ui/Ingreso";
import {
  AUTH_ERROR_CLASS,
  AUTH_INPUT_CLASS,
  AUTH_LABEL_CLASS,
  AUTH_STATUS_CLASS,
  AUTH_SUBMIT_CLASS,
} from "./AuthCard";

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
      <p role="status" className={AUTH_STATUS_CLASS}>
        Si el correo está registrado, enviaremos instrucciones de recuperación.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="grid gap-4" aria-label="Recuperar contraseña" noValidate>
      <div>
        <label htmlFor="fp-email" className={AUTH_LABEL_CLASS}>
          Correo electrónico
        </label>
        <div className="relative">
          <Mail className={INGRESO_ICONO_CLS} aria-hidden="true" />
          <input
            id="fp-email"
            type="email"
            autoComplete="username"
            className={AUTH_INPUT_CLASS}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
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
        {loading ? "Enviando…" : "Enviar instrucciones"}
      </button>
    </form>
  );
}

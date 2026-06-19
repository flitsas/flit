"use client";

// Pantalla "recordar usuario" (HU #10204). Valida el identificador (documento) en
// cliente (AC2) y, si es válido, solicita el recordatorio (POST /auth/remember-username)
// mostrando siempre una confirmación genérica (AC1, anti-enumeración).
import { useState } from "react";
import { rememberUsername } from "@/lib/api/auth";

const DOCUMENT_PATTERN = /^\d{4,20}$/;
const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

export function RememberUsernameForm() {
  const [document, setDocument] = useState("");
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    const value = document.trim();
    if (!value) {
      setError("Ingresa tu número de documento.");
      return;
    }
    if (!DOCUMENT_PATTERN.test(value)) {
      setError("El documento debe contener solo números (entre 4 y 20 dígitos).");
      return;
    }

    setLoading(true);
    try {
      await rememberUsername(value);
    } catch {
      // Confirmación genérica intencional.
    } finally {
      setLoading(false);
      setSent(true);
    }
  }

  if (sent) {
    return (
      <p role="status" className="text-sm text-slate-700">
        Si el documento corresponde a una cuenta, enviaremos el usuario al correo registrado.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label="Recordar usuario" noValidate>
      <div>
        <label htmlFor="ru-document" className="block text-sm font-medium text-[#162744] mb-1">
          Número de documento
        </label>
        <input
          id="ru-document"
          inputMode="numeric"
          aria-invalid={error ? true : undefined}
          className={INPUT_CLASS}
          value={document}
          onChange={(e) => setDocument(e.target.value)}
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
        {loading ? "Enviando…" : "Recordar mi usuario"}
      </button>
    </form>
  );
}

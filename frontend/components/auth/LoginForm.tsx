"use client";

// Formulario de login real (HU #10172, AC1). Llama a la API, almacena el JWT y
// notifica el éxito para redirigir. Maneja credenciales inválidas (401) y cuenta
// bloqueada temporalmente (403, HU #10170).
import Link from "next/link";
import { useState } from "react";
import { loginUser } from "@/lib/api/auth";
import { rememberEmail, storeToken } from "@/lib/auth/session";

const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20";

export interface LoginFormProps {
  onSuccess?: () => void;
  defaultEmail?: string;
}

export function LoginForm({ onSuccess, defaultEmail = "" }: LoginFormProps) {
  const [email, setEmail] = useState(defaultEmail);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    if (!email.trim() || !password) {
      setError("Ingresa tu correo y contraseña.");
      return;
    }

    setLoading(true);
    try {
      const result = await loginUser(email.trim(), password);
      storeToken(result.accessToken);
      rememberEmail(email.trim());
      onSuccess?.();
    } catch (err) {
      const status = (err as { status?: number }).status;
      setError(
        status === 403
          ? "Tu cuenta está bloqueada temporalmente. Contacta a tu administrador."
          : "Correo o contraseña incorrectos.",
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" aria-label="Iniciar sesión" noValidate>
      <div>
        <label htmlFor="login-email" className="block text-sm font-medium text-[#162744] mb-1">
          Correo electrónico
        </label>
        <input
          id="login-email"
          type="email"
          autoComplete="username"
          className={INPUT_CLASS}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </div>

      <div>
        <label htmlFor="login-password" className="block text-sm font-medium text-[#162744] mb-1">
          Contraseña
        </label>
        <input
          id="login-password"
          type="password"
          autoComplete="current-password"
          className={INPUT_CLASS}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
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
        {loading ? "Ingresando…" : "Iniciar sesión"}
      </button>

      <div className="flex justify-between text-sm">
        <Link href="/auth/forgot-password" className="text-[#557eff] hover:underline">
          ¿Olvidaste tu contraseña?
        </Link>
        <Link href="/auth/remember-username" className="text-[#557eff] hover:underline">
          ¿Olvidaste tu usuario?
        </Link>
      </div>
    </form>
  );
}

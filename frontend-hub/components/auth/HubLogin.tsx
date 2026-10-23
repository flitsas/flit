"use client";

import { useState } from "react";
import Link from "next/link";
import { ExternalLink, Lock, Mail, ShieldAlert, User as UserIcon } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { postJson } from "@/lib/api";
import { ParticlesCanvas } from "./ParticlesCanvas";

// Login del hub (A-06, HU #12991). Mismo diseño y mensajes que el de Trámites (components/atom/Login.tsx), pero
// no guarda tokens en el navegador: POST /connect/login abre la sesión del hub (cookie HttpOnly) y se vuelve a la
// petición que pidió el login —normalmente /connect/authorize de un producto— o al inicio del hub.

interface NetworkRedirectInfo {
  networkDomain: string;
  loginUrl: string;
}

interface LoginError {
  code?: string;
  error?: string;
  networkDomain?: string;
  loginUrl?: string;
  returnUrl?: string | null;
}

type Panel = "form" | "blocked" | "roleDeactivated" | "network";

export function HubLogin({ returnUrl, navigate = (url) => window.location.assign(url) }: { returnUrl?: string; navigate?: (url: string) => void }) {
  const [email, setEmail] = useState("");
  const [pass, setPass] = useState("");
  const [error, setError] = useState("");
  const [panel, setPanel] = useState<Panel>("form");
  const [network, setNetwork] = useState<NetworkRedirectInfo | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    if (!email.trim() || !pass) {
      setError("Ingresa tu correo y contraseña.");
      return;
    }

    setLoading(true);
    try {
      const result = await postJson<LoginError>("/connect/login", { email: email.trim(), password: pass, returnUrl });
      if (result.ok) {
        // El servidor solo devuelve rutas relativas del mismo host (evita un redireccionamiento abierto).
        navigate(result.body?.returnUrl ?? "/");
        return;
      }

      const body = result.body ?? {};
      if (result.status === 403 && body.error === "NETWORK_DOMAIN_REQUIRED" && body.networkDomain && body.loginUrl) {
        // ADR-0060 D3: usuario de una red con dominio propio; se ofrece el enlace, sin redirigir solo.
        setNetwork({ networkDomain: body.networkDomain, loginUrl: body.loginUrl });
        setPanel("network");
      } else if (body.code === "ALL_ROLES_INACTIVE") {
        setPanel("roleDeactivated");
      } else if (result.status === 403) {
        setPanel("blocked");
      } else if (result.status === 401) {
        setError("Correo o contraseña incorrectos.");
      } else {
        setError("No fue posible iniciar sesión. Inténtalo de nuevo.");
      }
    } catch {
      setError("No fue posible iniciar sesión. Inténtalo de nuevo.");
    } finally {
      setLoading(false);
    }
  }

  const back = (
    <button type="button" onClick={() => setPanel("form")} className="mt-3 block text-xs font-semibold text-flit-brand transition hover:opacity-80">
      ← Volver a intentar
    </button>
  );

  return (
    <div className="flex h-screen w-full flex-col overflow-hidden md:flex-row">
      <div
        className="relative order-1 flex min-h-[260px] w-full flex-col items-center justify-center overflow-hidden md:min-h-0 md:w-7/12"
        style={{ background: "linear-gradient(120deg,var(--color-flit-tech) 0%,var(--color-flit-brand) 100%)" }}
      >
        <ParticlesCanvas />
        <div className="relative z-10 flex flex-col items-center px-8 text-center">
          <BrandLogo variant="white" className="h-auto w-full max-w-[280px] object-contain md:max-w-[360px]" />
        </div>
      </div>

      <div className="order-2 flex w-full flex-col justify-center overflow-y-auto bg-white px-6 py-10 sm:px-12 md:w-5/12 lg:px-16">
        <div className="mx-auto w-full max-w-sm">
          <div className="mb-8 flex flex-col items-center gap-3">
            <div className="flex h-16 w-16 items-center justify-center rounded-full bg-flit-tech">
              <UserIcon className="h-8 w-8 text-white" strokeWidth={2.2} />
            </div>
            <h1 className="text-2xl font-bold text-flit-brand">Iniciar Sesión</h1>
          </div>

          {panel === "blocked" || panel === "roleDeactivated" ? (
            <div className="flex gap-3 rounded-xl border border-flit-alert bg-flit-alert/5 p-5" role="alert">
              <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-flit-alert" />
              <div className="text-sm">
                <p className="font-semibold text-flit-alert">Acceso Restringido</p>
                <p className="mt-1 text-xs text-slate-600">
                  {panel === "blocked"
                    ? "Tu cuenta está bloqueada temporalmente. Contacta a tu administrador para restablecer el acceso."
                    : "Tu rol ha sido desactivado y no puedes ingresar al sistema. Contacta a tu administrador para resolver este problema."}
                </p>
                {back}
              </div>
            </div>
          ) : panel === "network" && network ? (
            <div className="flex gap-3 rounded-xl border border-flit-brand bg-flit-brand/5 p-5" role="alert" aria-live="assertive">
              <ExternalLink className="mt-0.5 h-5 w-5 shrink-0 text-flit-brand" />
              <div className="text-sm">
                <p className="font-semibold text-flit-brand">Tu red tiene un dominio propio</p>
                <p className="mt-1 text-xs text-slate-600">
                  Ingresa desde <strong>{network.networkDomain}</strong> para acceder a tu cuenta.
                </p>
                <a href={network.loginUrl} rel="noopener" className="mt-3 inline-flex items-center gap-1 text-xs font-semibold text-flit-brand underline hover:opacity-80">
                  Ir a {network.networkDomain}
                </a>
                {back}
              </div>
            </div>
          ) : (
            <form onSubmit={submit} className="space-y-4" aria-label="Iniciar sesión" noValidate>
              <div>
                <label htmlFor="login-email" className="mb-1.5 block text-xs font-medium text-slate-600">Usuario Corporativo</label>
                <div className="relative">
                  <Mail className="absolute left-3 top-3.5 h-4 w-4 text-slate-400" />
                  <input
                    id="login-email"
                    type="email"
                    autoComplete="username"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="usuario@flit.io"
                    aria-invalid={error ? true : undefined}
                    className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-10 pr-3 text-sm outline-none transition focus:border-flit-brand focus:ring-2 focus:ring-flit-brand/20"
                  />
                </div>
              </div>
              <div>
                <label htmlFor="login-password" className="mb-1.5 block text-xs font-medium text-slate-600">Contraseña</label>
                <div className="relative">
                  <Lock className="absolute left-3 top-3.5 h-4 w-4 text-slate-400" />
                  <input
                    id="login-password"
                    type="password"
                    autoComplete="current-password"
                    value={pass}
                    onChange={(e) => setPass(e.target.value)}
                    placeholder="••••••••"
                    aria-invalid={error ? true : undefined}
                    className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-10 pr-3 text-sm outline-none transition focus:border-flit-brand focus:ring-2 focus:ring-flit-brand/20"
                  />
                </div>
                <Link href="/auth/forgot-password" className="mt-2 inline-block text-xs text-slate-500 transition hover:text-flit-brand">
                  ¿Olvidó su contraseña?
                </Link>
              </div>

              {error && (
                <p role="alert" className="flex items-center gap-2 text-xs text-flit-alert">
                  <ShieldAlert className="h-3.5 w-3.5" /> {error}
                </p>
              )}

              <button type="submit" disabled={loading} className="w-full rounded-xl bg-flit-brand py-3 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60">
                {loading ? "Ingresando…" : "Iniciar Sesión"}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}

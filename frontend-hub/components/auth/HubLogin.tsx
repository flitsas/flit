"use client";

import { useState } from "react";
import Link from "next/link";
import { ExternalLink, Lock, Mail, ShieldAlert } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { postJson } from "@/lib/api";
import { buttonClass } from "@flit/ui/Button";
import {
  IngresoLayout,
  INGRESO_AVISO_CLS,
  INGRESO_BOTON_CLS,
  INGRESO_CONTROL_CLS,
  INGRESO_ENLACE_CLS,
  INGRESO_ERROR_CLS,
  INGRESO_ETIQUETA_CLS,
  INGRESO_ICONO_CLS,
  INGRESO_TITULO_CLS,
} from "@flit/ui/Ingreso";

// Login del hub (A-06, HU #12991). Mismo diseño (IngresoLayout de @flit/ui, propuesta «Diseño Inteligente», HU #14485) y mensajes que el de Trámites (components/atom/Login.tsx), pero
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
    <button type="button" onClick={() => setPanel("form")} className="mt-3 block text-xs font-semibold text-[color:var(--di-action)] transition hover:opacity-80">
      ← Volver a intentar
    </button>
  );

  return (
    <IngresoLayout logo={<BrandLogo variant="white" />} logoTarjeta={<BrandLogo variant="auto" alt="" />}>
      <h1 className={INGRESO_TITULO_CLS}>Iniciar Sesión</h1>

      {panel === "blocked" || panel === "roleDeactivated" ? (
        <div className={`${INGRESO_AVISO_CLS} border border-[color:var(--di-danger)] bg-[color:var(--di-danger-soft)]`} role="alert">
          <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-[color:var(--di-danger)]" />
          <div className="text-sm">
            <p className="font-semibold text-[color:var(--di-danger)]">Acceso Restringido</p>
            <p className="mt-1 text-xs text-[color:var(--di-text-secondary)]">
              {panel === "blocked"
                ? "Tu cuenta está bloqueada temporalmente. Contacta a tu administrador para restablecer el acceso."
                : "Tu rol ha sido desactivado y no puedes ingresar al sistema. Contacta a tu administrador para resolver este problema."}
            </p>
            {back}
          </div>
        </div>
      ) : panel === "network" && network ? (
        <div className={`${INGRESO_AVISO_CLS} border border-[color:var(--di-action)] bg-[color:var(--di-accent-soft)]`} role="alert" aria-live="assertive">
          <ExternalLink className="mt-0.5 h-5 w-5 shrink-0 text-[color:var(--di-action)]" />
          <div className="text-sm">
            <p className="font-semibold text-[color:var(--di-heading)]">Tu red tiene un dominio propio</p>
            <p className="mt-1 text-xs text-[color:var(--di-text-secondary)]">
              Ingresa desde <strong>{network.networkDomain}</strong> para acceder a tu cuenta.
            </p>
            <a href={network.loginUrl} rel="noopener" className="mt-3 inline-flex items-center gap-1 text-xs font-semibold text-[color:var(--di-action)] underline hover:opacity-80">
              Ir a {network.networkDomain}
            </a>
            {back}
          </div>
        </div>
      ) : (
        <form onSubmit={submit} className="grid gap-4" aria-label="Iniciar sesión" noValidate>
          <div>
            <label htmlFor="login-email" className={INGRESO_ETIQUETA_CLS}>Usuario Corporativo</label>
            <div className="relative">
              <Mail className={INGRESO_ICONO_CLS} />
              <input
                id="login-email"
                type="email"
                autoComplete="username"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="usuario@flit.io"
                aria-invalid={error ? true : undefined}
                className={INGRESO_CONTROL_CLS}
              />
            </div>
          </div>
          <div>
            <label htmlFor="login-password" className={INGRESO_ETIQUETA_CLS}>Contraseña</label>
            <div className="relative">
              <Lock className={INGRESO_ICONO_CLS} />
              <input
                id="login-password"
                type="password"
                autoComplete="current-password"
                value={pass}
                onChange={(e) => setPass(e.target.value)}
                placeholder="••••••••"
                aria-invalid={error ? true : undefined}
                className={INGRESO_CONTROL_CLS}
              />
            </div>
            <Link href="/auth/forgot-password" className={INGRESO_ENLACE_CLS}>
              ¿Olvidó su contraseña?
            </Link>
          </div>

          {error && (
            <p role="alert" className={INGRESO_ERROR_CLS}>
              <ShieldAlert className="h-3.5 w-3.5" /> {error}
            </p>
          )}

          <button type="submit" disabled={loading} className={buttonClass({ className: INGRESO_BOTON_CLS })}>
            {loading ? "Ingresando…" : "Iniciar Sesión"}
          </button>
        </form>
      )}
    </IngresoLayout>
  );
}

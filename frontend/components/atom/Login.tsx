"use client";

import { useState } from "react";
import Link from "next/link";
import { loginUser } from "@/lib/api/auth";
import { rememberEmail, storeToken } from "@/lib/auth/session";
import { BrandLogo } from "@/components/brand/BrandLogo";
import { FLIT_PRIVACY_POLICY_URL } from "@/lib/brand/types";
import { ShieldAlert, Mail, ExternalLink } from "lucide-react";
import { buttonClass } from "@flit/ui/Button";
import {
  CampoClave,
  IngresoAyuda,
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

/**
 * HU #12424 AC2 — datos del 403 `NETWORK_DOMAIN_REQUIRED` (ADR-0060 D3). Vienen tal cual del
 * servidor; el cliente no decide nada de acceso, solo pinta el mensaje y el enlace.
 */
interface NetworkRedirectInfo {
  networkDomain: string;
  loginUrl: string;
}

// Login real del diseño base (Feature #10113, HU #10172). El aspecto es el ingreso de la propuesta
// «Diseño Inteligente» (IngresoLayout de @flit/ui, HU #14485); cablea la autenticación al backend:
// loginUser() → JWT en cookie+storage → onAuthenticated(). El 2FA queda fuera de
// alcance del Feature, así que el flujo es de un solo paso (credenciales).
export function Login({
  onAuthenticated,
  defaultEmail = "",
}: {
  onAuthenticated: () => void;
  defaultEmail?: string;
}) {
  const [email, setEmail] = useState(defaultEmail);
  const [pass, setPass] = useState("");
  const [error, setError] = useState("");
  const [blocked, setBlocked] = useState(false);
  const [roleDeactivated, setRoleDeactivated] = useState(false);
  const [networkRedirect, setNetworkRedirect] = useState<NetworkRedirectInfo | null>(null);
  const [loading, setLoading] = useState(false);

  async function submitCreds(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setBlocked(false);
    setRoleDeactivated(false);
    setNetworkRedirect(null);

    if (!email.trim() || !pass) {
      setError("Ingresa tu correo y contraseña.");
      return;
    }

    setLoading(true);
    try {
      const result = await loginUser(email.trim(), pass);
      storeToken(result.accessToken);
      rememberEmail(email.trim());
      onAuthenticated();
    } catch (err) {
      const apiErr = err as {
        status?: number;
        body?: { code?: string; error?: string; networkDomain?: string; loginUrl?: string };
      };
      if (
        apiErr.status === 403 &&
        apiErr.body?.error === "NETWORK_DOMAIN_REQUIRED" &&
        apiErr.body.networkDomain &&
        apiErr.body.loginUrl
      ) {
        // HU #12424 AC2 (ADR-0060 D3) — credencial válida de un usuario de una red MARCA_BLANCA
        // presentada por el dominio de FLIT: se ofrece el enlace directo, sin redirigir solo.
        // Ninguna decisión de acceso se toma aquí — el servidor ya la tomó.
        setNetworkRedirect({ networkDomain: apiErr.body.networkDomain, loginUrl: apiErr.body.loginUrl });
      } else if (apiErr.body?.code === "ALL_ROLES_INACTIVE") {
        // HU #10511 — todos los roles del usuario fueron desactivados: mensaje
        // distinto al de bloqueo temporal (HU #10170), para no confundir la causa.
        setRoleDeactivated(true);
      } else if (apiErr.status === 403) {
        // Cuenta bloqueada temporalmente (HU #10170): panel de acceso restringido.
        setBlocked(true);
      } else {
        // HU #12424 AC3 — credencial incorrecta y usuario fuera de la red responden IGUAL
        // (401 INVALID_CREDENTIALS, sin código distintivo): mismo texto de siempre, sin ramas.
        setError("Correo o contraseña incorrectos.");
      }
    } finally {
      setLoading(false);
    }
  }

  const aviso = "border border-[color:var(--di-danger)] bg-[color:var(--di-danger-soft)]";
  const volver = "mt-3 text-xs font-semibold text-[color:var(--di-action)] transition hover:opacity-80";

  return (
    <IngresoLayout
      logo={<BrandLogo variant="white" />}
      logoTarjeta={<BrandLogo variant="auto" alt="" />}
      pie={
        <a
          href={FLIT_PRIVACY_POLICY_URL}
          target="_blank"
          rel="noopener noreferrer"
          className="text-[color:var(--di-ingreso-texto-2)] transition hover:text-[color:var(--di-ingreso-texto)]"
        >
          Políticas de Privacidad y Términos de Uso
        </a>
      }
    >
      <h1 className={INGRESO_TITULO_CLS}>Iniciar Sesión</h1>

      {blocked ? (
        <div className={`${INGRESO_AVISO_CLS} ${aviso} animate-fade-in`} role="alert">
          <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-[color:var(--di-danger)]" />
          <div className="text-sm">
            <p className="font-semibold text-[color:var(--di-danger)]">Acceso Restringido</p>
            <p className="mt-1 text-xs text-[color:var(--di-text-secondary)]">Tu cuenta está bloqueada temporalmente. Contacta a tu administrador para restablecer el acceso.</p>
            <button type="button" onClick={() => setBlocked(false)} className={volver}>
              ← Volver a intentar
            </button>
          </div>
        </div>
      ) : roleDeactivated ? (
        <div className={`${INGRESO_AVISO_CLS} ${aviso} animate-fade-in`} role="alert">
          <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-[color:var(--di-danger)]" />
          <div className="text-sm">
            <p className="font-semibold text-[color:var(--di-danger)]">Acceso Restringido</p>
            <p className="mt-1 text-xs text-[color:var(--di-text-secondary)]">Tu rol ha sido desactivado y no puedes ingresar al sistema. Contacta a tu administrador para resolver este problema.</p>
            <button type="button" onClick={() => setRoleDeactivated(false)} className={volver}>
              ← Volver a intentar
            </button>
          </div>
        </div>
      ) : networkRedirect ? (
        <div
          className={`${INGRESO_AVISO_CLS} animate-fade-in border border-[color:var(--di-action)] bg-[color:var(--di-accent-soft)]`}
          role="alert"
          aria-live="assertive"
        >
          <ExternalLink className="mt-0.5 h-5 w-5 shrink-0 text-[color:var(--di-action)]" />
          <div className="text-sm">
            <p className="font-semibold text-[color:var(--di-heading)]">Tu red tiene un dominio propio</p>
            <p className="mt-1 text-xs text-[color:var(--di-text-secondary)]">
              Ingresa desde <strong>{networkRedirect.networkDomain}</strong> para acceder a tu cuenta.
            </p>
            <a
              href={networkRedirect.loginUrl}
              rel="noopener"
              className="mt-3 inline-flex items-center gap-1 text-xs font-semibold text-[color:var(--di-action)] underline hover:opacity-80"
            >
              Ir a {networkRedirect.networkDomain}
            </a>
            <button type="button" onClick={() => setNetworkRedirect(null)} className={`block ${volver}`}>
              ← Volver a intentar
            </button>
          </div>
        </div>
      ) : (
        <form onSubmit={submitCreds} className="grid gap-4 animate-fade-in" aria-label="Iniciar sesión" noValidate>
          <div>
            <label htmlFor="login-email" className={INGRESO_ETIQUETA_CLS}>Correo</label>
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
            <CampoClave
              id="login-password"
              autoComplete="current-password"
              value={pass}
              onChange={(e) => setPass(e.target.value)}
              placeholder="••••••••"
              aria-invalid={error ? true : undefined}
            />
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
          <IngresoAyuda />
        </form>
      )}
    </IngresoLayout>
  );
}

"use client";

// Modal global de sesión expirada (HU #10172, AC2). Escucha el evento emitido por
// el cliente HTTP cuando la API responde SESSION_EXPIRED y redirige a /login
// preservando la ruta actual como returnUrl. Accesible (role=dialog, aria-modal).
//
// Con la sesión de la suite (FLIT_SESSION_MODE=oidc) casi nunca es que la sesión venció: es que esta cookie quedó de
// una sesión que se cerró desde otro producto, o de otro usuario que ya no es el del hub. Entonces se pide una sesión
// nueva al hub sin mostrar nada (si el hub tiene sesión, el usuario ni lo nota); el modal queda para cuando eso ya se
// intentó hace poco y la API sigue rechazando.
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { reauthenticate } from "@flit/auth/client";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth/session";
import { isOidcSession } from "@/lib/auth/session-mode";
import { buttonClass } from "@flit/ui/Button";
import { MODAL_VELO_CLS } from "@flit/ui/Modal";

export function SessionExpiredListener() {
  const [open, setOpen] = useState(false);
  const [reconnecting, setReconnecting] = useState(false);
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    const handler = () => {
      if (isOidcSession() && reauthenticate()) {
        setReconnecting(true);
        return;
      }
      setOpen(true);
    };
    window.addEventListener(SESSION_EXPIRED_EVENT, handler);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler);
  }, []);

  function goToLogin() {
    setOpen(false);
    const returnUrl = encodeURIComponent(pathname || "/");
    router.push(`/login?returnUrl=${returnUrl}`);
  }

  if (reconnecting) {
    // HU #13004 — mientras se pide la sesión nueva al hub: tapa la pantalla para que no se vean errores sueltos.
    return (
      <div
        className="fixed inset-0 z-[100] flex flex-col items-center justify-center gap-3 bg-[color:color-mix(in_srgb,var(--di-surface)_95%,transparent)] backdrop-blur-sm"
        role="status"
        aria-live="polite"
      >
        <span className="h-8 w-8 animate-spin rounded-full border-2 border-[color:var(--di-action)] border-t-transparent" aria-hidden="true" />
        <p className="text-sm font-medium text-[color:var(--di-heading)]">Reconectando tu sesión…</p>
      </div>
    );
  }

  if (!open) {
    return null;
  }

  return (
    <div
      className={`fixed inset-0 z-[100] flex items-center justify-center ${MODAL_VELO_CLS} px-4`}
      role="dialog"
      aria-modal="true"
      aria-labelledby="session-expired-title"
      aria-describedby="session-expired-desc"
    >
      {/* Homologación (HU #14511): repintado en su sitio con la franja, el radio y el botón del Modal. */}
      <div className="w-full max-w-md overflow-clip rounded-[var(--di-radius-modal)] bg-card text-card-foreground shadow-[var(--shadow-modal)]">
        <div className="relative overflow-clip bg-[color:var(--di-accent)] px-5 py-4 sm:px-6 sm:py-5">
          <span
            aria-hidden="true"
            className="pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]"
          />
          <h2 id="session-expired-title" className="relative z-[1] text-lg font-bold text-[color:var(--di-on-accent)]">
            Tu sesión expiró
          </h2>
        </div>
        <div className="p-5 sm:p-6">
          <p id="session-expired-desc" className="text-sm text-muted-foreground">
            Por seguridad, tu sesión se cerró. Vuelve a iniciar sesión para continuar.
          </p>
          <button
            type="button"
            autoFocus
            onClick={goToLogin}
            className={buttonClass({ className: "mt-5 w-full" })}
          >
            Ir a iniciar sesión
          </button>
        </div>
      </div>
    </div>
  );
}

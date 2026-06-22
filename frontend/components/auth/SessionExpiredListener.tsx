"use client";

// Modal global de sesión expirada (HU #10172, AC2). Escucha el evento emitido por
// el cliente HTTP cuando la API responde SESSION_EXPIRED y redirige a /login
// preservando la ruta actual como returnUrl. Accesible (role=dialog, aria-modal).
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { SESSION_EXPIRED_EVENT } from "@/lib/auth/session";

export function SessionExpiredListener() {
  const [open, setOpen] = useState(false);
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    const handler = () => setOpen(true);
    window.addEventListener(SESSION_EXPIRED_EVENT, handler);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler);
  }, []);

  function goToLogin() {
    setOpen(false);
    const returnUrl = encodeURIComponent(pathname || "/");
    router.push(`/login?returnUrl=${returnUrl}`);
  }

  if (!open) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="session-expired-title"
      aria-describedby="session-expired-desc"
    >
      <div className="bg-white rounded-2xl w-full max-w-md p-6 shadow-2xl">
        <h2 id="session-expired-title" className="text-lg font-semibold text-[#162744]">
          Tu sesión expiró
        </h2>
        <p id="session-expired-desc" className="mt-2 text-sm text-slate-600">
          Por seguridad, tu sesión se cerró. Vuelve a iniciar sesión para continuar.
        </p>
        <button
          type="button"
          autoFocus
          onClick={goToLogin}
          className="mt-5 w-full rounded-xl py-3 text-sm font-semibold text-white transition hover:opacity-90"
          style={{ background: "#557eff" }}
        >
          Ir a iniciar sesión
        </button>
      </div>
    </div>
  );
}

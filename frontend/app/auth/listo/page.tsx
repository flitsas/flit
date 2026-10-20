"use client";

import { useEffect, useState } from "react";
import { safeRelative } from "@/lib/auth/safe-relative";
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

// A-10 (HU #13001): paso intermedio tras el login por el hub. Guarda en localStorage los claims SIN firma
// (/auth/claims) —lo que ya leen menús, permisos y guardias— y borra la cookie del token antiguo, si quedó. El token
// real sigue solo en el servidor.
export default function SesionLista() {
  const [error, setError] = useState(false);

  useEffect(() => {
    const to = safeRelative(new URLSearchParams(window.location.search).get("to"));
    fetch("/auth/claims", { credentials: "same-origin", cache: "no-store" })
      .then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        const { claimsToken } = (await response.json()) as { claimsToken: string };
        window.localStorage.setItem(TOKEN_STORAGE_KEY, claimsToken);
        document.cookie = `${TOKEN_COOKIE}=; path=/; Max-Age=0; SameSite=Lax`;
        window.location.replace(to);
      })
      .catch(() => setError(true));
  }, []);

  return (
    <main className="flex min-h-screen items-center justify-center bg-flit-bg px-4">
      <p role={error ? "alert" : "status"} className="text-sm text-slate-600">
        {error ? (
          <>
            No fue posible abrir la sesión. <a className="text-flit-brand underline" href="/auth/login">Intentar de nuevo</a>
          </>
        ) : (
          "Abriendo tu sesión…"
        )}
      </p>
    </main>
  );
}

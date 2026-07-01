"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { getToken } from "@/lib/api/client";
import { clearToken } from "@/lib/auth/session";
import { hasActiveSession } from "@/lib/auth/guard";

export interface AuthGate {
  authed: boolean;
  hydrated: boolean;
  logout: () => void;
}

/**
 * Gate de auth compartido por los layouts "SPA-style" (/, /tramites, /admin/*).
 * Verifica sesión activa (token presente y no expirado) al montar; si es inválida,
 * limpia el token y navega a /login preservando la ruta como returnUrl — así una
 * ventana abierta con sesión vencida siempre cae en /login, no en contenido protegido.
 * logout() navega a /login SIN returnUrl para que el siguiente login caiga en home,
 * no en la ruta que se estaba viendo (a diferencia de la expiración detectada a
 * mitad de sesión vía SessionExpiredListener, donde sí se quiere volver ahí).
 */
export function useAuthGate(): AuthGate {
  const router = useRouter();
  const pathname = usePathname();
  const [authed, setAuthed] = useState(false);
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    const active = hasActiveSession(getToken());
    if (!active) {
      clearToken();
      router.replace(`/login?returnUrl=${encodeURIComponent(pathname || "/")}`);
    }
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setAuthed(active);
    setHydrated(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function logout() {
    clearToken();
    router.replace("/login");
  }

  return { authed, hydrated, logout };
}

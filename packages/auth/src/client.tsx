"use client";

// @flit/auth en el navegador (contrato §8): la sesión se pregunta al servidor de la app (/auth/session); el token
// nunca llega aquí.
import { useEffect, useState } from "react";
import type { SessionUser } from "./types";

export type { SessionUser } from "./types";

export function useSession(): { user: SessionUser | null; status: "loading" | "ready" } {
  const [state, setState] = useState<{ user: SessionUser | null; status: "loading" | "ready" }>({ user: null, status: "loading" });

  useEffect(() => {
    let cancelled = false;
    fetch("/auth/session", { credentials: "same-origin", cache: "no-store" })
      .then(async (response) => (response.ok ? ((await response.json()) as SessionUser) : null))
      .catch(() => null)
      .then((user) => {
        if (!cancelled) setState({ user, status: "ready" });
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}

/** Lleva al login del hub y vuelve a la ruta actual. */
export function signIn(returnTo: string = window.location.pathname + window.location.search): void {
  window.location.assign(`/auth/login?returnTo=${encodeURIComponent(returnTo)}`);
}

export function signOut(): void {
  window.location.assign("/auth/logout");
}

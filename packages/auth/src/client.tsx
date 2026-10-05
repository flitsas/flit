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

const REAUTH_KEY = "flit:reauth-at";
/**
 * Si ya se pidió una sesión nueva hace menos de esto y vuelve a fallar, el problema no es la sesión: no se insiste. Un
 * bucle real (la API rechaza todo token) da la vuelta completa en 1-3 s; cambiar de usuario a mano (salir, escribir la
 * contraseña, volver) tarda más de 10 s. Con 60 s, dos cambios de usuario seguidos mostraban el aviso sin razón.
 */
const REAUTH_WINDOW_MS = 10_000;
/** Varias llamadas pueden fallar a la vez: la primera redirige y las demás no deben abrir el aviso mientras tanto. */
let redirecting = false;

/**
 * La sesión local de la app ya no sirve (se cerró en otro producto, o entró otro usuario en el hub): se pide una nueva
 * al hub sin mostrar nada. Si el hub tiene sesión, vuelve a `returnTo` con el usuario actual; si no, el hub pide la
 * contraseña (`silent`: vuelve con `sso=0`). Devuelve `false` sin redirigir si ya se intentó hace unos segundos, para no entrar
 * en un bucle cuando la API rechaza el token por otra razón; la app muestra entonces su aviso de sesión expirada.
 */
export function reauthenticate({
  returnTo = window.location.pathname + window.location.search,
  silent = false,
}: { returnTo?: string; silent?: boolean } = {}): boolean {
  if (redirecting) return true;
  const now = Date.now();
  try {
    const last = Number(window.sessionStorage.getItem(REAUTH_KEY));
    if (last && now - last < REAUTH_WINDOW_MS) return false;
    window.sessionStorage.setItem(REAUTH_KEY, String(now));
  } catch {
    // Sin sessionStorage (modo privado estricto) no hay cómo frenar un bucle: mejor el aviso que redirigir a ciegas.
    return false;
  }
  redirecting = true;
  window.location.assign(`/auth/login?${silent ? "prompt=none&" : ""}returnTo=${encodeURIComponent(returnTo)}`);
  return true;
}

/** `true` mientras la app va camino al hub a pedir una sesión nueva (la página está por recargarse). */
export function isReauthenticating(): boolean {
  return redirecting;
}

export function signOut(): void {
  window.location.assign("/auth/logout");
}

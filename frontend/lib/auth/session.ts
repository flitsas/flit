// Gestión de sesión en cliente (HU #10172): almacenamiento del JWT en cookie +
// localStorage y señal de sesión expirada para el modal global.
import { clearDrFlitSession } from "@/components/dr-flit/dr-flit-session-store";
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "./jwt";

/** Evento que dispara el modal de "sesión expirada" cuando la API devuelve SESSION_EXPIRED. */
export const SESSION_EXPIRED_EVENT = "flit:session-expired";

const LEGACY_AUTHED_KEY = "flit:authed";
const REMEMBERED_EMAIL_KEY = "flit:remembered-email";

/** Persiste el JWT tras un login exitoso (cookie legible por middleware + localStorage). */
export function storeToken(token: string): void {
  if (typeof document !== "undefined") {
    document.cookie = `${TOKEN_COOKIE}=${token}; path=/; SameSite=Lax`;
  }
  if (typeof window !== "undefined") {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, token);
    window.localStorage.setItem(LEGACY_AUTHED_KEY, "1");
  }
}

/** Limpia el JWT (logout o sesión expirada). */
export function clearToken(): void {
  if (typeof document !== "undefined") {
    document.cookie = `${TOKEN_COOKIE}=; path=/; Max-Age=0; SameSite=Lax`;
  }
  if (typeof window !== "undefined") {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    window.localStorage.removeItem(LEGACY_AUTHED_KEY);
    // DR. FLIT no debe sobrevivir a un cierre de sesión (ni a token inválido).
    clearDrFlitSession();
  }
}

/** Notifica globalmente que la sesión expiró (lo escucha SessionExpiredListener). */
export function emitSessionExpired(): void {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT));
  }
}

/** Recuerda el último correo usado para iniciar sesión (HU #10172/#10204). */
export function rememberEmail(email: string): void {
  if (typeof window !== "undefined") {
    window.localStorage.setItem(REMEMBERED_EMAIL_KEY, email);
  }
}

export function getRememberedEmail(): string {
  if (typeof window !== "undefined") {
    return window.localStorage.getItem(REMEMBERED_EMAIL_KEY) ?? "";
  }
  return "";
}

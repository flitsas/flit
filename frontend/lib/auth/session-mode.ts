// Modo de sesión de Trámites (A-10, HU #13001), en el navegador. Lo fija el layout raíz en <html data-session-mode>
// desde FLIT_SESSION_MODE (runtime, sin rebuild):
//   legacy (por defecto) → JWT en la cookie flit_token y en localStorage, como siempre.
//   oidc                 → sesión de @flit/auth en el servidor; el navegador solo guarda los claims SIN firma para
//                          dibujar menús y permisos, y la API se llama por el mismo origen (/api/v1 → BFF).
export const SESSION_MODE_ATTRIBUTE = "data-session-mode";

export function isOidcSession(): boolean {
  return typeof document !== "undefined" && document.documentElement.getAttribute(SESSION_MODE_ATTRIBUTE) === "oidc";
}

// Tope absoluto de la sesión de caja (ADR-0082): 12 horas desde que la persona se autenticó, aunque el refresh siga vivo.
// El BFF impone el mismo tope; aquí se corta antes para llevar a la persona al login en lugar de mostrarle errores.
export const TOPE_SESION_CAJA_HORAS = 12;

/** Claims (`header.payload.`) → instante de autenticación en segundos (`auth_time`), o null si el token no lo trae. */
export function inicioDeSesion(claimsToken: string): number | null {
  const payload = claimsToken.split(".")[1];
  if (!payload) return null;
  try {
    const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
    const claims = JSON.parse(atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), "="))) as { auth_time?: unknown };
    return typeof claims.auth_time === "number" ? claims.auth_time : null;
  } catch {
    return null;
  }
}

export function sesionDeCajaVencida(claimsToken: string, ahoraSegundos = Math.floor(Date.now() / 1000)): boolean {
  const inicio = inicioDeSesion(claimsToken);
  // Fail-closed: sin `auth_time` legible no hay forma de acotar la sesión, así que se da por vencida.
  return inicio === null || ahoraSegundos - inicio > TOPE_SESION_CAJA_HORAS * 3600;
}

/** Cookies de la sesión de la caja (la cifrada se parte en trozos `nombre`, `nombre.1`, …). */
export const COOKIES_SESION_CAJA = ["flit_session_caja", ...[1, 2, 3, 4].map((i) => `flit_session_caja.${i}`)];

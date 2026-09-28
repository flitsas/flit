// Cookies de @flit/auth. Contrato §8: HttpOnly, SameSite=Lax y NUNCA con atributo Domain (cada host tiene su
// sesión). Una sesión cifrada puede pasar de 4 KB (permisos del token): se parte en trozos nombre, nombre.1, …

// Por producto: en producción cada app tiene su host, pero en local todas comparten `localhost` o `127.0.0.1` (las
// cookies no distinguen puerto) y la sesión del hub pisaría la de Trámites.
export const sessionCookie = (productCode: string): string => `flit_session_${productCode}`;
export const txCookie = (productCode: string): string => `flit_oidc_tx_${productCode}`;

/** Margen por trozo: los navegadores aceptan ~4096 bytes por cookie, incluidos nombre y atributos. */
const CHUNK = 3800;
const MAX_CHUNKS = 5;

export interface CookieOptions {
  secure: boolean;
  maxAgeSeconds: number;
}

export function serializeCookie(name: string, value: string, options: CookieOptions): string {
  const parts = [`${name}=${value}`, "Path=/", "HttpOnly", "SameSite=Lax", `Max-Age=${options.maxAgeSeconds}`];
  if (options.secure) parts.push("Secure");
  return parts.join("; ");
}

/** Set-Cookie de un valor partido en trozos, más el borrado de los trozos sobrantes de una sesión anterior. */
export function chunkedCookies(name: string, value: string, options: CookieOptions): string[] {
  const chunks: string[] = [];
  for (let i = 0; i < value.length; i += CHUNK) chunks.push(value.slice(i, i + CHUNK));
  if (chunks.length > MAX_CHUNKS) throw new Error("La sesión no cabe en las cookies.");
  const headers = chunks.map((chunk, i) => serializeCookie(chunkName(name, i), chunk, options));
  for (let i = chunks.length; i < MAX_CHUNKS; i++) headers.push(serializeCookie(chunkName(name, i), "", { ...options, maxAgeSeconds: 0 }));
  return headers;
}

export function clearChunkedCookies(name: string, secure: boolean): string[] {
  return Array.from({ length: MAX_CHUNKS }, (_, i) => serializeCookie(chunkName(name, i), "", { secure, maxAgeSeconds: 0 }));
}

export function parseCookies(header: string | null): Map<string, string> {
  const map = new Map<string, string>();
  for (const part of (header ?? "").split(";")) {
    const i = part.indexOf("=");
    if (i > 0) map.set(part.slice(0, i).trim(), part.slice(i + 1).trim());
  }
  return map;
}

/** Une los trozos presentes; `null` si no hay sesión. */
export function readChunked(cookies: Map<string, string> | { get(name: string): { value: string } | undefined }, name: string): string | null {
  let value = "";
  for (let i = 0; i < MAX_CHUNKS; i++) {
    const raw = cookies instanceof Map ? cookies.get(chunkName(name, i)) : cookies.get(chunkName(name, i))?.value;
    if (!raw) break;
    value += raw;
  }
  return value || null;
}

function chunkName(name: string, index: number): string {
  return index === 0 ? name : `${name}.${index}`;
}

// Decodificación de JWT en el borde (Edge) y en cliente — SIN verificar la firma.
// El gate de UI (middleware) solo necesita leer claims; la API valida la firma
// criptográficamente (HU #10194, AC6). No confiar en esto para autorización real.

export interface JwtPayload {
  sub?: string;
  role?: string;
  roles?: string[];
  exp?: number;
  [key: string]: unknown;
}

/** Nombre de la cookie que transporta el JWT al borde (legible por middleware). */
export const TOKEN_COOKIE = "flit_token";

/** Clave de localStorage de respaldo para el JWT en cliente. */
export const TOKEN_STORAGE_KEY = "flit:jwt";

/** Rol requerido para la consola de administración de compañías. */
export const SUPER_ADMIN_ROLE = "SuperAdmin";

/**
 * Decodifica el payload (segunda parte) de un JWT base64url. Devuelve `null` si
 * el token es vacío, malformado o no es JSON válido. Tolerante a entornos sin
 * `atob` (Node) usando `Buffer` como respaldo.
 */
export function decodeJwtPayload(token: string | null | undefined): JwtPayload | null {
  if (!token) {
    return null;
  }

  const segments = token.split(".");
  if (segments.length < 2) {
    return null;
  }

  try {
    const json = base64UrlDecode(segments[1]);
    const payload = JSON.parse(json) as JwtPayload;
    return typeof payload === "object" && payload !== null ? payload : null;
  } catch {
    return null;
  }
}

/**
 * Indica si el payload contiene el rol SuperAdmin (comparación case-insensitive
 * sobre `role` o el arreglo `roles`).
 */
export function isSuperAdmin(payload: JwtPayload | null): boolean {
  if (!payload) {
    return false;
  }

  const target = SUPER_ADMIN_ROLE.toLowerCase();
  if (typeof payload.role === "string" && payload.role.toLowerCase() === target) {
    return true;
  }

  return Array.isArray(payload.roles) && payload.roles.some((r) => r?.toLowerCase() === target);
}

function base64UrlDecode(value: string): string {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), "=");

  if (typeof atob === "function") {
    const binary = atob(padded);
    // Reconstituye UTF-8 a partir de los bytes latin1 que devuelve atob.
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  }

  // Respaldo Node (tests/SSR sin atob).
  return Buffer.from(padded, "base64").toString("utf8");
}

// Decodificación de JWT en el borde (Edge) y en cliente — SIN verificar la firma.
// El gate de UI (middleware) solo necesita leer claims; la API valida la firma
// criptográficamente (HU #10194, AC6). No confiar en esto para autorización real.

/** Item del claim `roles` (HU #10506): un objeto `{id, code}` por cada rol activo del usuario. */
export interface JwtRoleClaim {
  id?: string;
  code?: string;
}

export interface JwtPayload {
  sub?: string;
  role?: string;
  role_code?: string;
  /** Catálogo global multi-rol (HU #10506) — array de objetos, no de strings. */
  roles?: JwtRoleClaim[];
  permissions?: string[];
  tenant_id?: string;
  tenant_name?: string;
  role_id?: string;
  exp?: number;
  [key: string]: unknown;
}

/** Nombre de la cookie que transporta el JWT al borde (legible por middleware). */
export const TOKEN_COOKIE = "flit_token";

/** Clave de localStorage de respaldo para el JWT en cliente. */
export const TOKEN_STORAGE_KEY = "flit:jwt";

/** Rol requerido para la consola de administración global. */
export const SUPER_ADMIN_ROLE = "SuperAdmin";

/** Rol requerido para administración de empresa. */
export const ADMIN_COMPANY_ROLE = "AdminCompany";

/** Rol requerido para la consola OT (HU #10218). */
export const OT_ADMIN_ROLE = "ot_admin";

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
  if (typeof payload.role_code === "string" && payload.role_code.toLowerCase() === target) {
    return true;
  }
  if (typeof payload.role === "string" && payload.role.toLowerCase() === target) {
    return true;
  }
  return (
    Array.isArray(payload.roles) &&
    payload.roles.some((r) => typeof r?.code === "string" && r.code.toLowerCase() === target)
  );
}

/**
 * Indica si el payload contiene el rol AdminCompany (comparación case-insensitive
 * sobre `role`/`role_code` o el arreglo `roles`, para usuarios multi-rol — HU #10506).
 */
export function isAdminCompany(payload: JwtPayload | null): boolean {
  if (!payload) {
    return false;
  }

  const target = ADMIN_COMPANY_ROLE.toLowerCase();
  if (typeof payload.role_code === "string" && payload.role_code.toLowerCase() === target) {
    return true;
  }
  if (typeof payload.role === "string" && payload.role.toLowerCase() === target) {
    return true;
  }
  return (
    Array.isArray(payload.roles) &&
    payload.roles.some((r) => typeof r?.code === "string" && r.code.toLowerCase() === target)
  );
}

/** Slug del permiso que gobierna el módulo LOG QX (HU #10794/#10795). */
export const LOG_QX_READ_PERMISSION = "logqx.read";

/**
 * Indica si el payload contiene un slug de permiso en el claim `permissions`
 * (array de strings; comparación case-insensitive). El JWT es la fuente de verdad
 * de permisos en runtime — no confiar para autorización real (la API valida).
 */
export function hasPermission(payload: JwtPayload | null, slug: string): boolean {
  if (!payload || !Array.isArray(payload.permissions)) {
    return false;
  }
  const target = slug.toLowerCase();
  return payload.permissions.some(
    (p) => typeof p === "string" && p.toLowerCase() === target,
  );
}

/**
 * Puede leer el LOG QX (HU #10795): tiene el permiso `logqx.read` o es SuperAdmin
 * (bypass total, igual que el gate del backend en `PermissionAuthorizationHandler`).
 */
export function canReadLogQx(payload: JwtPayload | null): boolean {
  return isSuperAdmin(payload) || hasPermission(payload, LOG_QX_READ_PERMISSION);
}

/**
 * Indica si el payload contiene el rol ot_admin (comparación case-insensitive).
 */
export function isOtAdmin(payload: JwtPayload | null): boolean {
  if (!payload) {
    return false;
  }

  const target = OT_ADMIN_ROLE.toLowerCase();
  if (typeof payload.role === "string" && payload.role.toLowerCase() === target) {
    return true;
  }

  return (
    Array.isArray(payload.roles) &&
    payload.roles.some((r) => typeof r?.code === "string" && r.code.toLowerCase() === target)
  );
}

function base64UrlDecode(value: string): string {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized.padEnd(normalized.length + ((4 - (normalized.length % 4)) % 4), "=");

  if (typeof atob === "function") {
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  }

  return Buffer.from(padded, "base64").toString("utf8");
}

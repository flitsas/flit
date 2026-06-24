// Lógica pura del gate de acceso a /admin/* y /empresa/* (HU #10194, AC6; HU #10218 OT admin).
// Extraída del middleware para poder probarla sin el runtime de Next.js.
import { decodeJwtPayload, isAdminCompany, isOtAdmin, isSuperAdmin } from "./jwt";

export const FORBIDDEN_PATH = "/403";

export type UserRole = "superadmin" | "admincompany" | "user";

export interface AdminAccessDecision {
  /** `true` si el token corresponde a un usuario con acceso permitido. */
  allowed: boolean;
  /** Ruta de redirección cuando `allowed` es `false`. */
  redirectTo?: string;
}

/**
 * Evalúa si un token habilita el acceso a la consola admin.
 *
 * Reglas:
 * - Sin token o token malformado → no renderizar, redirigir a /403.
 * - SuperAdmin → permitido en todo /admin/*.
 * - ot_admin → permitido solo en /admin/transit-offices/* (HU #10218).
 * - Otros roles → redirigir a /403.
 */
export function evaluateAdminAccess(
  token: string | null | undefined,
  pathname?: string,
): AdminAccessDecision {
  const payload = decodeJwtPayload(token);

  if (payload && isSuperAdmin(payload)) {
    return { allowed: true };
  }

  if (
    pathname?.startsWith("/admin/transit-offices") &&
    payload &&
    isOtAdmin(payload)
  ) {
    return { allowed: true };
  }

  return { allowed: false, redirectTo: FORBIDDEN_PATH };
}

/**
 * Evalúa si un token habilita el acceso a la sección de empresa (/empresa/*).
 * Permitido para AdminCompany y SuperAdmin.
 */
export function evaluateEmpresaAccess(token: string | null | undefined): AdminAccessDecision {
  const payload = decodeJwtPayload(token);

  if (payload && (isSuperAdmin(payload) || isAdminCompany(payload))) {
    return { allowed: true };
  }

  return { allowed: false, redirectTo: FORBIDDEN_PATH };
}

/**
 * Devuelve el rol del usuario a partir del token JWT.
 */
export function getUserRole(token: string | null | undefined): UserRole {
  const payload = decodeJwtPayload(token);
  if (payload && isSuperAdmin(payload)) return "superadmin";
  if (payload && isAdminCompany(payload)) return "admincompany";
  return "user";
}

/** Ruta de inicio (dashboard) a la que vuelve un usuario ya autenticado. */
export const HOME_PATH = "/";

/**
 * Indica si el token representa una sesión activa: decodifica y, si trae `exp`,
 * verifica que no haya expirado. No valida la firma (eso lo hace la API).
 */
export function hasActiveSession(token: string | null | undefined): boolean {
  const payload = decodeJwtPayload(token);
  if (!payload) {
    return false;
  }

  if (typeof payload.exp === "number" && payload.exp * 1000 <= Date.now()) {
    return false;
  }

  return true;
}

export interface LoginAccessDecision {
  /** `true` si el login NO debe mostrarse y hay que redirigir. */
  redirect: boolean;
  /** Destino de la redirección cuando `redirect` es `true`. */
  redirectTo?: string;
}

/**
 * Evalúa el acceso a /login. Si ya existe sesión activa, redirige al dashboard
 * (no tiene sentido volver a mostrar el login). Sin sesión, permite renderizarlo.
 */
export function evaluateLoginAccess(token: string | null | undefined): LoginAccessDecision {
  if (hasActiveSession(token)) {
    return { redirect: true, redirectTo: HOME_PATH };
  }

  return { redirect: false };
}

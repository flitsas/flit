// Lógica pura del gate de acceso a /admin/* y /empresa/* (HU #10194, AC6; HU #10218 OT admin).
// Extraída del middleware para poder probarla sin el runtime de Next.js.
//
// HU #13650 (Feature #13102, ADR-0074): QUIÉN entra a cada ruta ya no vive aquí sino en la tabla única
// `lib/nav/module-access.ts` (`ROUTE_ACCESS`), la misma que decide el menú. Este archivo solo valida la sesión y
// traduce la decisión a una redirección.
import { decodeJwtPayload, isAdminCompany, isOtUser, isSuperAdmin } from "./jwt";
import { matchRouteRule, resolveRouteAccess } from "@/lib/nav/module-access";

/** Destino de los fallos de SESIÓN (sin token, malformado o vencido). */
export const FORBIDDEN_PATH = "/403";

/** Raíz de Administración → Plataforma → Confirmación RUNT (Feature #12276). */
export const RUNT_CONFIRMATION_BASE_PATH = "/admin/plataforma/confirmacion-runt";

export type UserRole = "superadmin" | "admincompany" | "ot_admin" | "user";

/** Parámetros con los que se vuelve al inicio cuando la persona tiene sesión pero no el permiso (ADR-0074 §4). */
export const ACCESS_DENIED_PARAM = "acceso";
export const ACCESS_DENIED_VALUE = "denegado";
export const ACCESS_DENIED_MODULE_PARAM = "modulo";

/** `/?acceso=denegado&modulo=<id>`: el inicio muestra «No tienes acceso a este módulo.» y limpia la marca. */
export function accessDeniedPath(moduleId?: string): string {
  const query = new URLSearchParams({ [ACCESS_DENIED_PARAM]: ACCESS_DENIED_VALUE });
  if (moduleId) query.set(ACCESS_DENIED_MODULE_PARAM, moduleId);
  return `/?${query.toString()}`;
}

export interface AdminAccessDecision {
  /** `true` si el token corresponde a un usuario con acceso permitido. */
  allowed: boolean;
  /**
   * Ruta de redirección cuando `allowed` es `false`: `/403` si la sesión no es válida; el inicio con la marca de
   * acceso denegado si hay sesión válida pero falta el permiso (HU #13650).
   */
  redirectTo?: string;
}

/** Decisión común: sesión válida + regla de la tabla para el pathname (`fallbackPath` si ninguna regla lo cubre). */
function evaluateRouteAccess(
  token: string | null | undefined,
  pathname: string,
  fallbackPath: string,
): AdminAccessDecision {
  if (!hasActiveSession(token)) {
    return { allowed: false, redirectTo: FORBIDDEN_PATH };
  }

  const claims = decodeJwtPayload(token);
  // Una ruta que ninguna regla cubre se trata como la raíz de su sección: deny-by-default.
  const effectivePath = matchRouteRule(pathname) ? pathname : fallbackPath;
  const decision = resolveRouteAccess(claims, effectivePath);
  return decision.allowed ? { allowed: true } : { allowed: false, redirectTo: accessDeniedPath(decision.moduleId) };
}

/**
 * Evalúa si un token habilita el acceso a la consola admin (`/admin/*`). Las reglas por ruta están en
 * `ROUTE_ACCESS` (SuperAdmin en todo /admin; organismo de tránsito en /admin/transit-offices con sus excepciones;
 * AdminCompany en /admin/companies; permisos para generación documental y Confirmación RUNT; Banners solo SuperAdmin).
 *
 * - Sin token, token malformado o expirado → /403.
 * - Sesión válida sin el permiso → inicio con el aviso de acceso denegado.
 */
export function evaluateAdminAccess(
  token: string | null | undefined,
  pathname?: string,
): AdminAccessDecision {
  return evaluateRouteAccess(token, pathname ?? "/admin", "/admin");
}

/**
 * Evalúa si un token habilita el acceso a la sección de empresa (/empresa/*): AdminCompany y SuperAdmin. Sin sesión
 * activa → /403; con sesión sin el rol → inicio con el aviso.
 */
export function evaluateEmpresaAccess(token: string | null | undefined): AdminAccessDecision {
  return evaluateRouteAccess(token, "/empresa", "/empresa");
}

/**
 * Devuelve el rol del usuario a partir del token JWT.
 */
export function getUserRole(token: string | null | undefined): UserRole {
  const payload = decodeJwtPayload(token);
  if (payload && isSuperAdmin(payload)) return "superadmin";
  if (payload && isAdminCompany(payload)) return "admincompany";
  if (payload && isOtUser(payload)) return "ot_admin";
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

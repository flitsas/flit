// Lógica pura del gate de acceso a /admin/* y /empresa/* (HU #10194, AC6; HU #10218 OT admin).
// Extraída del middleware para poder probarla sin el runtime de Next.js.
import {
  canAccessRuntConfirmation,
  canManageBanners,
  canReadGeneracionDocumental,
  decodeJwtPayload,
  isAdminCompany,
  isOtUser,
  isSuperAdmin,
} from "./jwt";

export const FORBIDDEN_PATH = "/403";

/** Raíz de Administración → Plataforma → Confirmación RUNT (Feature #12276). */
export const RUNT_CONFIRMATION_BASE_PATH = "/admin/plataforma/confirmacion-runt";

export type UserRole = "superadmin" | "admincompany" | "ot_admin" | "user";

/**
 * HU #12856 (Feature #12847) — Reglas, Requisitos y Configuración del organismo quedan
 * exclusivos de Super Admin también en la capa de UI: un `ot_admin` (o cualquier otro rol de un
 * tenant OT) que entra por URL directa a una de estas 3 sub-rutas debe caer a /403, aunque el
 * resto de `/admin/transit-offices/*` le siga permitido (HU #10218). Bloqueo en dos capas: la API
 * ya exige la policy `SuperAdmin` (HU-B2, backend); esto cierra el hueco de UI.
 */
const SUPERADMIN_ONLY_OT_SUBROUTES = ["rules", "requirements", "configuracion"] as const;

/**
 * Decodifica los segmentos percent-encoded de un pathname (p. ej. `%72ules` → `rules`) sin
 * lanzar ante secuencias inválidas: si `decodeURIComponent` falla, se usa el pathname original
 * tal cual, de forma que un valor malformado nunca abre una vía de bypass del gate.
 */
function safeDecodePathname(pathname: string): string {
  try {
    return decodeURIComponent(pathname);
  } catch {
    return pathname;
  }
}

function isSuperAdminOnlyOtRoute(pathname: string): boolean {
  const normalized = safeDecodePathname(pathname).toLowerCase();
  return SUPERADMIN_ONLY_OT_SUBROUTES.some((segment) =>
    new RegExp(`^/admin/transit-offices/[^/]+/${segment}(/|$)`).test(normalized),
  );
}

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
 * - Sin token, token malformado o token expirado → no renderizar, redirigir a /403.
 * - SuperAdmin → permitido en todo /admin/*.
 * - Usuario de un tenant OT (ot_admin o entity_type TRANSIT_OFFICE) → permitido en
 *   /admin/transit-offices/* (HU #10218), salvo /rules, /requirements y /configuracion, que
 *   quedan exclusivos de Super Admin (HU #12856, Feature #12847).
 * - AdminCompany → permitido en /admin/companies/* (HU #11228; la página redirige a su tenant).
 * - Cualquier rol con `generacion-documental.read` → permitido en /admin/generacion-documental/* (Feature #12201).
 * - Cualquier rol con `runt_confirmation.settings.manage` o `runt_confirmation.history.read` →
 *   permitido en /admin/plataforma/confirmacion-runt/* (Feature #12276).
 * - Cualquier rol con `banners.manage` → permitido en /admin/banners/* (Feature #12236, HU #12241).
 * - Otros roles → redirigir a /403.
 */
export function evaluateAdminAccess(
  token: string | null | undefined,
  pathname?: string,
): AdminAccessDecision {
  if (!hasActiveSession(token)) {
    return { allowed: false, redirectTo: FORBIDDEN_PATH };
  }

  const payload = decodeJwtPayload(token);

  if (payload && isSuperAdmin(payload)) {
    return { allowed: true };
  }

  if (
    pathname?.startsWith("/admin/transit-offices") &&
    payload &&
    isOtUser(payload) &&
    !isSuperAdminOnlyOtRoute(pathname)
  ) {
    return { allowed: true };
  }

  if (
    pathname?.startsWith("/admin/companies") &&
    payload &&
    isAdminCompany(payload)
  ) {
    return { allowed: true };
  }

  // Generación documental (Feature #12201): el módulo NO es exclusivo de SuperAdmin. El
  // acceso se gobierna por el permiso `generacion-documental.read` del JWT, no por rol —
  // si se dejara solo el gate SuperAdmin de arriba, un AdminCompany con el módulo
  // habilitado sería redirigido a /403 antes de renderizar nada.
  if (
    pathname?.startsWith("/admin/generacion-documental") &&
    canReadGeneracionDocumental(payload)
  ) {
    return { allowed: true };
  }

  // Confirmación RUNT (Feature #12276): único submódulo de Plataforma que NO es exclusivo de
  // SuperAdmin. Se abre con cualquiera de sus dos permisos; qué pestaña ve cada uno lo decide la
  // propia página. El resto de /admin/plataforma/* sigue siendo SuperAdmin.
  if (
    pathname?.startsWith(RUNT_CONFIRMATION_BASE_PATH) &&
    canAccessRuntConfirmation(payload)
  ) {
    return { allowed: true };
  }

  // Banners promocionales (Feature #12236, HU #12241): tampoco es exclusivo de SuperAdmin —
  // se gobierna por el permiso `banners.manage` del JWT, mismo patrón que generación documental.
  if (
    pathname?.startsWith("/admin/banners") &&
    canManageBanners(payload)
  ) {
    return { allowed: true };
  }

  return { allowed: false, redirectTo: FORBIDDEN_PATH };
}

/**
 * Evalúa si un token habilita el acceso a la sección de empresa (/empresa/*).
 * Permitido para AdminCompany y SuperAdmin. Sin sesión activa (sin token,
 * malformado o expirado) → /403.
 */
export function evaluateEmpresaAccess(token: string | null | undefined): AdminAccessDecision {
  if (!hasActiveSession(token)) {
    return { allowed: false, redirectTo: FORBIDDEN_PATH };
  }

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

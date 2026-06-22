// Lógica pura del gate de acceso a /admin/* (HU #10194, AC6). Extraída del
// middleware para poder probarla sin el runtime de Next.js.
import { decodeJwtPayload, isSuperAdmin } from "./jwt";

export const FORBIDDEN_PATH = "/403";

export interface AdminAccessDecision {
  /** `true` si el token corresponde a un SuperAdmin válido. */
  allowed: boolean;
  /** Ruta de redirección cuando `allowed` es `false`. */
  redirectTo?: string;
}

/**
 * Evalúa si un token habilita el acceso a la consola admin.
 *
 * Reglas:
 * - Sin token o token malformado → no renderizar, redirigir a /403.
 * - Rol distinto de SuperAdmin → redirigir a /403.
 * - SuperAdmin → permitido.
 *
 * Uso de ejemplo:
 * ```ts
 * const { allowed, redirectTo } = evaluateAdminAccess(req.cookies.get("flit_token")?.value);
 * if (!allowed) return NextResponse.redirect(new URL(redirectTo!, req.url));
 * ```
 */
export function evaluateAdminAccess(token: string | null | undefined): AdminAccessDecision {
  const payload = decodeJwtPayload(token);

  if (payload && isSuperAdmin(payload)) {
    return { allowed: true };
  }

  return { allowed: false, redirectTo: FORBIDDEN_PATH };
}

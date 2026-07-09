// Resolución de módulos de la SPA (home). Extraído de app/page.tsx para poder probar la
// regla de "Ayuda universal" sin montar toda la página.
import type { ModuleId } from "@/components/atom/Shell";

/** Todos los módulos conocidos (fallback cuando aún no hay permisos RBAC cargados). */
export const ALL_MODULE_IDS: ModuleId[] = [
  "dashboard",
  "tramites",
  "reportes",
  "validaciones",
  "usuarios",
  "ayuda",
  "rbac",
  "auditoria",
];

/**
 * Módulos "universales" para la resolución de `?m=` (NO confundir con acceso universal de
 * datos): siempre se consideran un valor válido de módulo aunque el catálogo RBAC
 * (`accessibleCodes`, GET /api/v1/security/modules) no los incluya, para que `parseModule`
 * no rebote a "dashboard" tras `router.replace`. "Ayuda" es soporte real para cualquier
 * usuario. "Auditoría" (HU #10680) es SuperAdmin-only: no existe como fila en el catálogo
 * de módulos RBAC (a diferencia de "rbac", que sí la tiene) porque no se gobierna por
 * permisos sino por el claim SuperAdmin del JWT — igual que su botón en el dock (Shell.tsx,
 * bloque `currentUser?.isSuperAdmin`). Por eso el montaje real del componente en
 * `app/page.tsx` sigue gateado explícitamente por `isSuperAdmin`, y el backend
 * (`GET /api/v1/superadmin/audit`) exige `SuperAdminPolicy` de todos modos.
 */
export const UNIVERSAL_MODULE_IDS: ModuleId[] = ["ayuda", "auditoria"];

/**
 * Construye la lista de módulos válidos para la SPA: los accesibles por RBAC más los
 * universales (sin duplicar). Si aún no hay permisos, devuelve todos los conocidos.
 */
export function buildValidModules(accessibleCodes: ModuleId[]): ModuleId[] {
  if (accessibleCodes.length === 0) {
    return [...ALL_MODULE_IDS];
  }
  const merged = [...accessibleCodes];
  for (const id of UNIVERSAL_MODULE_IDS) {
    if (!merged.includes(id)) {
      merged.push(id);
    }
  }
  return merged;
}

/** Normaliza el `?m=` de la URL a un módulo válido; cae a "dashboard" si no lo es. */
export function parseModule(raw: string | null, valid: ModuleId[]): ModuleId {
  const allowed = valid.length > 0 ? valid : ALL_MODULE_IDS;
  return allowed.includes(raw as ModuleId) ? (raw as ModuleId) : "dashboard";
}

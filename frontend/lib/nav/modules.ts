// Resolución de módulos SPA navegables por `?m=` — misma regla que el dock (invariante dock ≡ URL).
import type { ModuleId } from "@/components/atom/Shell";

/** Catálogo conocido de ids SPA (referencia / tests). No autoriza por sí solo. */
export const ALL_MODULE_IDS: ModuleId[] = [
  "dashboard",
  "tramites",
  "reportes",
  "reportes-detallados",
  "validaciones",
  "usuarios",
  "ayuda",
  "rbac",
  "auditoria",
  "log-qx",
  "ict-logs",
];

/**
 * Ids del dock horizontal SPA (constante DOCK en Shell), sin dashboard (FAB) ni
 * entradas claim-only (rbac / auditoría / log-qx / ict-logs).
 */
export const SPA_DOCK_MODULE_IDS: readonly ModuleId[] = [
  "tramites",
  "reportes",
  "reportes-detallados",
  "validaciones",
  "usuarios",
  "ayuda",
] as const;

/**
 * Admin OT: pestañas del hub viven en el dock; se omiten los módulos SPA homónimos
 * para no duplicar píldoras ni deep-links `?m=` a esas SPA.
 * Fuente única dock ↔ URL (Shell.tsx consume este Set).
 */
export const OT_ADMIN_SPA_OMIT: ReadonlySet<string> = new Set([
  "tramites",
  "reportes",
  "reportes-detallados",
  "usuarios",
]);

/**
 * Único módulo universal de navegación: soporte real para cualquier usuario.
 * `auditoria` / `log-qx` / `ict-logs` NO son universales — solo si claims/permisos
 * del dock los mostrarían (SuperAdmin / logqx.read / ict.logs.read).
 */
export const UNIVERSAL_MODULE_IDS: ModuleId[] = ["ayuda"];

export type NavigableModulesContext = {
  accessibleCodes: readonly string[];
  isSuperAdmin: boolean;
  isOtAdmin: boolean;
  canReadLogQx: boolean;
  canReadIctLogs: boolean;
};

/**
 * Fuente única: ids navegables por `?m=` = exactamente lo que el dock mostraría
 * para el mismo contexto (RBAC codes + claims + reglas OT + ayuda).
 * Deny-by-default: `accessibleCodes=[]` NO abre el catálogo completo.
 * `/admin/*` no entra en este catálogo.
 */
export function resolveNavigableModuleIds(ctx: NavigableModulesContext): ModuleId[] {
  const ids = new Set<ModuleId>();

  // FAB Inicio — siempre disponible en la SPA.
  ids.add("dashboard");

  for (const id of SPA_DOCK_MODULE_IDS) {
    const allowedByRbac = id === "ayuda" || ctx.accessibleCodes.includes(id);
    if (!allowedByRbac) continue;
    if (ctx.isOtAdmin && OT_ADMIN_SPA_OMIT.has(id)) continue;
    ids.add(id);
  }

  // SuperAdmin: mismas entradas SPA del bloque isSuperAdmin en Shell (no rutas /admin).
  if (ctx.isSuperAdmin) {
    ids.add("rbac");
    ids.add("auditoria");
  }

  if (ctx.canReadLogQx) {
    ids.add("log-qx");
  }

  if (ctx.canReadIctLogs) {
    ids.add("ict-logs");
  }

  return Array.from(ids);
}

/**
 * Compatibilidad: RBAC codes + ayuda, sin claims.
 * Preferir `resolveNavigableModuleIds` cuando haya JWT/claims disponibles.
 * Deny-by-default: lista vacía → solo dashboard + ayuda.
 */
export function buildValidModules(accessibleCodes: ModuleId[]): ModuleId[] {
  return resolveNavigableModuleIds({
    accessibleCodes,
    isSuperAdmin: false,
    isOtAdmin: false,
    canReadLogQx: false,
    canReadIctLogs: false,
  });
}

/**
 * Normaliza `?m=` a un módulo válido.
 * Deny-by-default: lista vacía o id desconocido → "dashboard" (hold seguro; sin bypass ALL).
 */
export function parseModule(raw: string | null, valid: ModuleId[]): ModuleId {
  if (!raw || valid.length === 0) return "dashboard";
  return valid.includes(raw as ModuleId) ? (raw as ModuleId) : "dashboard";
}

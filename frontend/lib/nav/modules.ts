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
  "ict-reportes",
  "ict-trazabilidad",
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
 * `auditoria` / `log-qx` / `ict-logs` / `ict-reportes` / `ict-trazabilidad` NO son universales — solo si claims/permisos
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
    // Reportes ICT (HU #11619) comparte el gate de Log ICT: mismo público, distinto espacio.
    ids.add("ict-reportes");
    // Trazabilidad ICT (Feature #11814): mismo permiso ict.logs.read, tercer espacio del sistema.
    ids.add("ict-trazabilidad");
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

/** Resultado del gate SPA: evitar replace en bucle cuando `?m=` no está autorizado. */
export type SpaModuleAccessPlan = {
  module: ModuleId;
  denied: boolean;
  /** Solo true si hay que corregir la URL (una vez). */
  shouldReplaceUrl: boolean;
  replaceTo: "/?m=dashboard" | null;
};

/**
 * Decide módulo activo y si hace falta `router.replace`.
 * Si `?m=` está denegado → dashboard; `shouldReplaceUrl` solo si la URL aún no es dashboard
 * (evita loop infinito de navegación con useSearchParams / replace).
 */
/**
 * Si ya se puede decidir sobre el acceso a un módulo, o hay que seguir esperando.
 *
 * <p>Existe como función propia porque el fallo no estaba en QUÉ se decidía sino en CUÁNDO. El
 * guard esperaba a `modulesLoading`, y eso no basta por dos razones encadenadas:</p>
 *
 * <p>1. `useAccessibleModules(authed)` se llama con `authed` en false en el primer render —antes de
 * que la sesión hidrate— y con `enabled` en false el hook se declara en
 * `{ modules: [], loading: false }`: lista VACÍA anunciando que no está cargando.</p>
 *
 * <p>2. Y cuando `authed` pasa a true, el `setState` que pone `loading` en true ocurre DENTRO de un
 * efecto, así que en ese mismo render `modulesLoading` sigue valiendo el `false` anterior. Mirar
 * `loading` no distingue «no he preguntado» de «pregunté y no hay nada»; `ready` sí.</p>
 *
 * <p>El síntoma era entrar por URL directa a `/?m=reportes` y rebotar al dashboard con un aviso de
 * «no tienes acceso» falso, mientras que desde el dock funcionaba —ahí el catálogo ya resolvió—,
 * lo que hacía que pareciera aleatorio.</p>
 */
export function puedeDecidirAccesoAModulo(estado: {
  hydrated: boolean;
  authed: boolean;
  modulesReady: boolean;
}): boolean {
  return estado.hydrated && estado.authed && estado.modulesReady;
}

export function planSpaModuleAccess(
  raw: string | null,
  navigableIds: readonly ModuleId[],
): SpaModuleAccessPlan {
  const requested = raw && raw.length > 0 ? raw : null;
  const allowedList = navigableIds as ModuleId[];

  if (requested && !allowedList.includes(requested as ModuleId)) {
    const alreadyOnDashboard = raw === "dashboard";
    return {
      module: "dashboard",
      denied: true,
      shouldReplaceUrl: !alreadyOnDashboard,
      replaceTo: alreadyOnDashboard ? null : "/?m=dashboard",
    };
  }

  return {
    module: parseModule(raw, allowedList),
    denied: false,
    shouldReplaceUrl: false,
    replaceTo: null,
  };
}

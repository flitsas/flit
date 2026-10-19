// Resolución de módulos SPA navegables por `?m=` — misma regla que el dock (invariante dock ≡ URL).
import type { ModuleId } from "@/components/atom/Shell";
import { isOtUser, type JwtPayload } from "@/lib/auth/jwt";
import { canAccessModule } from "./module-access";

/** Catálogo conocido de ids SPA (referencia / tests). No autoriza por sí solo. */
export const ALL_MODULE_IDS: ModuleId[] = [
  "dashboard",
  "tramites",
  "reportes",
  "validaciones",
  "historial-placa",
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
  "validaciones",
  // HU #12194 — gate RBAC puro: solo aparece si `historial-placa` viene en accessibleCodes.
  "historial-placa",
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
  "usuarios",
]);

/**
 * HU #13582 (Épica #13271) — ids `?m=` retirados que aún llegan por marcadores o enlaces viejos, con su
 * dirección nueva. Se redirige antes del gate de permisos: es la nueva dirección la que decide el acceso.
 */
export const LEGACY_MODULE_REDIRECTS: Readonly<Record<string, string>> = {
  "reportes-detallados": "/?m=reportes&reportesTab=detallados",
};

/** Dirección nueva para un `?m=` retirado, o null si el id no está retirado. */
export function legacyModuleRedirect(raw: string | null): string | null {
  return raw ? (LEGACY_MODULE_REDIRECTS[raw] ?? null) : null;
}

/**
 * Único módulo universal de navegación: soporte real para cualquier usuario.
 * `auditoria` / `log-qx` / `ict-logs` / `ict-reportes` / `ict-trazabilidad` NO son universales — solo si claims/permisos
 * del dock los mostrarían (SuperAdmin / logqx.read / ict.trazabilidad.read / ict.reportes.read).
 */
export const UNIVERSAL_MODULE_IDS: ModuleId[] = ["ayuda"];

export type NavigableModulesContext = {
  /** Códigos de `GET /api/v1/security/modules`; `null`/vacío = catálogo sin cargar (deny-by-default). */
  accessibleCodes: readonly string[] | null;
  /** Claims del token (permisos efectivos, ADR-0071). `null` = ilegibles ⇒ solo inicio y ayuda. */
  claims: JwtPayload | null;
};

/** Orden estable de los ids navegables (el del dock). */
const NAVIGABLE_ORDER: readonly ModuleId[] = [
  "dashboard",
  ...SPA_DOCK_MODULE_IDS,
  "rbac",
  "auditoria",
  "log-qx",
  "ict-logs",
  "ict-reportes",
  "ict-trazabilidad",
];

/**
 * Fuente única: ids navegables por `?m=` = exactamente lo que el dock mostraría para el mismo contexto. Cada id se
 * decide con su requisito en `MODULE_ACCESS` (ADR-0072, `module-access.ts`); aquí no hay reglas propias, solo la
 * excepción de Admin OT (sus pestañas del hub reemplazan a las SPA homónimas).
 * Deny-by-default: sin claims o con el catálogo sin cargar NO se abre nada protegido; solo Inicio y Ayuda.
 * `/admin/*` no entra en este catálogo.
 */
export function resolveNavigableModuleIds(ctx: NavigableModulesContext): ModuleId[] {
  const subject = { claims: ctx.claims, accessibleCodes: ctx.accessibleCodes };
  const otUser = isOtUser(ctx.claims);

  return NAVIGABLE_ORDER.filter((id) => {
    // Inicio (FAB) y Ayuda: siempre disponibles en la SPA, incluso antes de que carguen los permisos.
    if (id === "dashboard" || UNIVERSAL_MODULE_IDS.includes(id)) return true;
    if (otUser && OT_ADMIN_SPA_OMIT.has(id)) return false;
    return canAccessModule(subject, id);
  });
}

/**
 * Compatibilidad: RBAC codes + ayuda, sin claims.
 * Preferir `resolveNavigableModuleIds` cuando haya JWT/claims disponibles.
 * Deny-by-default: sin claims → solo dashboard + ayuda.
 */
export function buildValidModules(accessibleCodes: ModuleId[]): ModuleId[] {
  return resolveNavigableModuleIds({ accessibleCodes, claims: null });
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

// Tabla declarativa ÚNICA de requisitos de acceso por módulo y por ruta (ADR-0074, Feature #13102).
//
// La consumen tres puntos, sin ramas sueltas en Shell / page / guard:
//   1. `resolveNavigableModuleIds` (`lib/nav/modules.ts`) → menú y `/?m=` (HU #13649, #13650).
//   2. `tramitesNav` → los campos de visibilidad del dock salen de `navGatingFor` (mismo requisito, otra forma); las
//      pestañas del organismo, de `otHubNavGating` (`OT_HUB_TAB_PERMISSION`, la misma tabla que sus rutas, HU #13655).
//   3. `lib/auth/guard.ts` / `middleware.ts` → `/admin/*` y `/empresa/*` (HU #13650), con `resolveRouteAccess`.
//
// Esto es defensa en profundidad de la NAVEGACIÓN: la autorización real la hace la API (ADR-0074 §6). Por eso cada
// requisito lleva `api`: qué exige la API para ese módulo (permiso, política de rol o solo sesión) y en qué archivo
// vive. `__tests__/module-access.test.ts` verifica (a) que el requisito del menú es al menos tan estricto como el de
// la API y (b) que el archivo del backend sigue declarando esa política: si la API cambia y la tabla no, falla.
//
// Deny-by-default: claims ilegibles, catálogo RBAC todavía sin cargar o módulo sin entrada ⇒ sin acceso. Este archivo
// no importa React ni nada del navegador: lo usa el middleware (Node).
import { type JwtPayload, hasPermission, hasRoleCode, isOtUser, isSuperAdmin } from "@/lib/auth/jwt";

// Códigos de rol del catálogo global (ADR-0023), iguales a `ADMIN_COMPANY_ROLE` / `OT_ADMIN_ROLE` de jwt.ts. Se repiten
// como literales para que este módulo no dependa de constantes en tiempo de carga.
const ADMIN_COMPANY_ROLE = "AdminCompany";
const OT_ADMIN_ROLE = "ot_admin";
/**
 * Slugs de los módulos del organismo de tránsito (Anexo A, `OtPermissionMap.cs`). El `ot_admin` los tiene todos; los
 * demás roles del organismo (p. ej. Gestor OT, HU #13655) los reciben por rol o como permiso individual del Admin OT.
 */
export const OT_PERMISSIONS = {
  tramites: "ot.tramites.manage",
  improntas: "ot.improntas.manage",
  reportes: "ot.reportes.manage",
  mandatos: "ot.mandatos.manage",
  mandatarios: "ot.mandatarios.manage",
  documentos: "ot.documentos.manage",
  usuarios: "ot.usuarios.manage",
  integracion: "ot.integracion.manage",
} as const;
/** Slugs OT que el Admin OT puede delegar por usuario (OtPermissionMap.cs). */
export const OT_DOCUMENTOS_PERMISSION = OT_PERMISSIONS.documentos;
export const OT_MANDATARIOS_PERMISSION = OT_PERMISSIONS.mandatarios;

/**
 * HU #13655 — pestaña del hub del organismo (`/admin/transit-offices/{id}/<pestaña>`) → permiso OT que la abre a un rol
 * del organismo que no es `ot_admin`. La misma tabla decide el dock (`otHubNavGating`) y la ruta (`ROUTE_ACCESS`).
 * Las pestañas exclusivas del SuperAdmin (Reglas, Requisitos, Configuración) no están aquí.
 */
export const OT_HUB_TAB_PERMISSION = {
  "client-procedures": OT_PERMISSIONS.tramites,
  "revocation-requests": OT_PERMISSIONS.tramites,
  documents: OT_PERMISSIONS.documentos,
  usuarios: OT_PERMISSIONS.usuarios,
  reportes: OT_PERMISSIONS.reportes,
  mandatos: OT_PERMISSIONS.mandatos,
  "imprint-validation": OT_PERMISSIONS.improntas,
  webhooks: OT_PERMISSIONS.integracion,
} as const;

export type OtHubDelegableTab = keyof typeof OT_HUB_TAB_PERMISSION;

/**
 * Campos de visibilidad del dock para una pestaña del organismo: el `ot_admin` la ve siempre (como hasta ahora); otro rol
 * del organismo, solo con el permiso OT de la pestaña en su token (HU #13655).
 */
export function otHubNavGating(tab: OtHubDelegableTab, isOtAdmin: boolean): { permission?: string[] } {
  return isOtAdmin ? {} : { permission: [OT_HUB_TAB_PERMISSION[tab]] };
}

/** Lo que la API exige para el módulo. `authenticated` = solo sesión (el menú puede ser más estricto, nunca menos). */
export type ApiGuard =
  | { kind: "permission"; slug: string; source: string }
  | { kind: "policy"; policy: "SuperAdmin" | "AdminCompany" | "OtAdmin" | "OtAdminOrSuperAdmin" | "OtModule"; source: string }
  | { kind: "authenticated"; source: string };

/**
 * Requisito de acceso. Todas las condiciones declaradas se exigen a la vez (AND); dentro de `permissionsAny` y
 * `rolesAny` basta una. El SuperAdmin pasa todo salvo lo que sea solo suyo (igual que `PermissionAuthorizationHandler`
 * y que `canSee` del dock).
 */
export interface AccessRequirement {
  /** Solo el SuperAdmin FLIT (aunque otro rol tenga el permiso: la API exige el rol). */
  superAdminOnly?: boolean;
  /** Código del módulo RBAC que tiene que venir en `GET /api/v1/security/modules` (derivado del permiso efectivo). */
  rbacModule?: string;
  /** Basta uno de estos permisos efectivos en el claim `permissions` del token. */
  permissionsAny?: readonly string[];
  /** Basta uno de estos roles del token. */
  rolesAny?: readonly string[];
  /**
   * Alternativa a `rolesAny`: el permiso OT que el Admin OT delega por usuario (Anexo A §1.4, HU #13639). Con él,
   * `rolesAny` deja de exigirse; la API lo respeta solo con `Security:OtPermissionEnforcement` encendido, que es
   * cuando ese permiso se puede otorgar.
   */
  delegatedBy?: string;
  /** Usuario de un organismo de tránsito (`entity_type = TRANSIT_OFFICE` u `ot_admin`). */
  transitOfficeUser?: boolean;
  /** Qué exige la API para este módulo. */
  api: ApiGuard;
}

/** Quién pregunta: claims del token y, si ya cargó, el catálogo de módulos accesibles (`null` = aún no cargó). */
export interface AccessSubject {
  claims: JwtPayload | null;
  accessibleCodes: readonly string[] | null;
}

const SECURITY_MODULES_SOURCE = "services/core-api/src/Flit.Api/Endpoints/SecurityEndpoints.cs";
const SUPERADMIN_GROUP_SOURCE = "services/core-api/src/Flit.Api/Endpoints/SuperAdmin/SuperAdminEndpointExtensions.cs";
const SEEDER_SOURCE = "services/core-api/src/Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs";
const ANALYTICS_SOURCE = "services/core-api/src/Flit.Api/Endpoints/Analytics/AnalyticsMetricsEndpoints.cs";
const TRAMITES_SOURCE = "services/core-api/src/Flit.Api/Endpoints/TramitesEndpoints.cs";

/**
 * Módulos de la SPA (`/?m=<id>`) y entradas del dock que dependen de claims. Un id sin entrada se niega.
 * `dashboard` y `ayuda` son los dos únicos sin requisito (cualquier persona con sesión): `ayuda` es el único módulo
 * universal de ADR-0047 y `dashboard` es el hold seguro.
 */
export const MODULE_ACCESS = {
  dashboard: { api: { kind: "authenticated", source: ANALYTICS_SOURCE } },
  ayuda: { api: { kind: "authenticated", source: ANALYTICS_SOURCE } },
  tramites: { rbacModule: "tramites", api: { kind: "authenticated", source: TRAMITES_SOURCE } },
  // La API de reportes (analytics) solo pide sesión + alcance de tenant; lo que gobierna el módulo es el permiso del
  // catálogo (`reportes.read`, seeder). El menú exige el módulo RBAC, que sale del permiso efectivo del token.
  reportes: { rbacModule: "reportes", api: { kind: "permission", slug: "reportes.read", source: SEEDER_SOURCE } },
  validaciones: { rbacModule: "validaciones", api: { kind: "permission", slug: "validaciones.read", source: SEEDER_SOURCE } },
  "historial-placa": {
    rbacModule: "historial-placa",
    api: { kind: "permission", slug: "historial-placa.read", source: "services/core-api/src/Flit.Api/Endpoints/Tramites/ProcedureInstanceEndpoints.cs" },
  },
  usuarios: { rbacModule: "usuarios", api: { kind: "permission", slug: "usuarios.manage", source: SEEDER_SOURCE } },
  // Política de rol pura (`/api/v1/superadmin/*`): el permiso no basta.
  rbac: { superAdminOnly: true, api: { kind: "policy", policy: "SuperAdmin", source: SUPERADMIN_GROUP_SOURCE } },
  auditoria: { superAdminOnly: true, api: { kind: "policy", policy: "SuperAdmin", source: SUPERADMIN_GROUP_SOURCE } },
  "log-qx": {
    permissionsAny: ["logqx.read"],
    api: { kind: "permission", slug: "logqx.read", source: "services/core-api/src/Flit.Api/Endpoints/AdminLogQxEndpoints.cs" },
  },
  // Bug #13445: Log ICT es solo del SuperAdmin; Trazabilidad y Reportes ICT van por su permiso propio.
  "ict-logs": {
    superAdminOnly: true,
    api: { kind: "policy", policy: "SuperAdmin", source: "services/core-api/src/Flit.Api/Endpoints/AdminIctJobCatalogEndpoints.cs" },
  },
  "ict-trazabilidad": {
    permissionsAny: ["ict.trazabilidad.read"],
    api: { kind: "permission", slug: "ict.trazabilidad.read", source: SEEDER_SOURCE },
  },
  "ict-reportes": {
    permissionsAny: ["ict.reportes.read"],
    api: { kind: "permission", slug: "ict.reportes.read", source: "services/core-api/src/Flit.Api/Endpoints/Analytics/IctReportsEndpoints.cs" },
  },
  // Entradas del dock que no son módulos de la SPA pero se gobiernan por la misma regla.
  "admin-generacion-documental": {
    rbacModule: "generacion-documental",
    permissionsAny: ["generacion-documental.read"],
    api: { kind: "permission", slug: "generacion-documental.read", source: "services/core-api/src/Flit.Api/Endpoints/AdminGeneracionDocumentalEndpoints.cs" },
  },
  "admin-confirmacion-runt": {
    permissionsAny: ["runt_confirmation.settings.manage", "runt_confirmation.history.read"],
    api: { kind: "permission", slug: "runt_confirmation.settings.manage", source: "services/core-api/src/Flit.Api/Endpoints/AdminRuntConfirmationEndpoints.cs" },
  },
  // Gestor: «Administración» de su compañía (`/admin/companies`); API con la política AdminCompany.
  "mi-empresa": { rolesAny: [ADMIN_COMPANY_ROLE], api: { kind: "policy", policy: "AdminCompany", source: SECURITY_MODULES_SOURCE } },
  "admin-network": { rolesAny: [ADMIN_COMPANY_ROLE], api: { kind: "policy", policy: "AdminCompany", source: SECURITY_MODULES_SOURCE } },
} as const satisfies Record<string, AccessRequirement>;

export type ModuleAccessKey = keyof typeof MODULE_ACCESS;

/** Requisito de un módulo; `undefined` si no tiene entrada (⇒ negado). */
export function moduleRequirement(key: string): AccessRequirement | undefined {
  return Object.prototype.hasOwnProperty.call(MODULE_ACCESS, key)
    ? (MODULE_ACCESS as Record<string, AccessRequirement>)[key]
    : undefined;
}

/**
 * Evalúa un requisito contra el sujeto. Función pura, deny-by-default: sin claims legibles, o con un requisito de
 * módulo RBAC y el catálogo aún sin cargar, niega.
 */
export function canAccess(subject: AccessSubject, requirement: AccessRequirement | undefined): boolean {
  if (!requirement) return false;
  const { claims, accessibleCodes } = subject;
  if (!claims) return false;

  const superAdmin = isSuperAdmin(claims);
  if (requirement.superAdminOnly) return superAdmin;

  // El módulo RBAC se exige también al SuperAdmin: la API le entrega «todos los módulos activos», así que un módulo
  // que no viene es uno que no existe o está apagado (misma regla que tenía el resolver antes de la tabla).
  if (requirement.rbacModule && !(accessibleCodes ?? []).includes(requirement.rbacModule)) return false;
  if (superAdmin) return true;

  if (requirement.transitOfficeUser && !isOtUser(claims)) return false;
  if (
    requirement.rolesAny &&
    !requirement.rolesAny.some((role) => hasRoleCode(claims, role)) &&
    !(requirement.delegatedBy && hasPermission(claims, requirement.delegatedBy))
  ) {
    return false;
  }
  if (requirement.permissionsAny && !requirement.permissionsAny.some((slug) => hasPermission(claims, slug))) return false;
  return true;
}

/** Atajo: ¿puede este sujeto entrar al módulo `key` de la tabla? Sin entrada ⇒ no. */
export function canAccessModule(subject: AccessSubject, key: string): boolean {
  return canAccess(subject, moduleRequirement(key));
}

/**
 * Los mismos requisitos con la forma que entiende el dock (`NavItem` de @flit/shell): así el menú no repite la regla.
 * Una entrada sin requisito de módulo/permiso/rol devuelve `{}` (la ve quien entra al producto).
 */
export function navGatingFor(key: ModuleAccessKey): {
  module?: string;
  permission?: string[];
  roles?: string[];
  superAdminOnly?: boolean;
} {
  const req: AccessRequirement = MODULE_ACCESS[key];
  return {
    ...(req.rbacModule ? { module: req.rbacModule } : {}),
    ...(req.permissionsAny ? { permission: [...req.permissionsAny] } : {}),
    ...(req.rolesAny ? { roles: [...req.rolesAny] } : {}),
    ...(req.superAdminOnly ? { superAdminOnly: true } : {}),
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Rutas de servidor (`/admin/*`, `/empresa/*`) — HU #13650. Solo claims: el middleware no llama a la API.
// ---------------------------------------------------------------------------------------------------------------

export interface RouteAccessRule {
  /** Identificador estable del módulo; viaja en `?modulo=` al redirigir. */
  id: string;
  /** Se prueba contra el pathname decodificado y en minúsculas. El primero que coincide decide. */
  pattern: RegExp;
  requirement: AccessRequirement;
}

const OT_PERMISSION_MAP_SOURCE = "services/core-api/src/Flit.Api/Authorization/OtPermissionMap.cs";

const ROUTE_API_ADMIN ={ kind: "policy", policy: "SuperAdmin", source: "services/core-api/src/Flit.Api/Endpoints/AdminCompaniesEndpoints.cs" } as const;

/**
 * Reglas por prefijo, de la más específica a la más general. Reproducen una a una las del guard de rol anterior
 * (HU #10194, #10218, #12856, #12860, #11228, Feature #12201 y #12276); lo nuevo es que viven en una tabla.
 */
export const ROUTE_ACCESS: readonly RouteAccessRule[] = [
  // HU #12856 — Reglas, Requisitos y Configuración del organismo: exclusivos de SuperAdmin.
  {
    id: "admin-transit-offices-config",
    pattern: /^\/admin\/transit-offices\/[^/]+\/(rules|requirements|configuracion)(\/|$)/,
    requirement: { superAdminOnly: true, api: ROUTE_API_ADMIN },
  },
  // Pestañas del organismo (HU #13655): ot_admin o SuperAdmin; otro rol del organismo solo con el permiso OT de la
  // pestaña. Documentos (HU #12860) ya era así desde HU #13639 con ot.documentos.manage.
  ...(Object.keys(OT_HUB_TAB_PERMISSION) as OtHubDelegableTab[]).map(
    (tab): RouteAccessRule => ({
      id: `admin-transit-offices-${tab}`,
      pattern: new RegExp(`^/admin/transit-offices/[^/]+/${tab}(/|$)`),
      requirement: {
        transitOfficeUser: true,
        rolesAny: [OT_ADMIN_ROLE],
        delegatedBy: OT_HUB_TAB_PERMISSION[tab],
        api:
          tab === "documents"
            ? { kind: "policy", policy: "OtAdminOrSuperAdmin", source: "services/core-api/src/Flit.Api/Endpoints/AdminOtEndpoints.cs" }
            : { kind: "permission", slug: OT_HUB_TAB_PERMISSION[tab], source: OT_PERMISSION_MAP_SOURCE },
      },
    }),
  ),
  // HU #10218 — el resto del hub del organismo (listado e Inicio del organismo): cualquier usuario de un organismo.
  {
    id: "admin-transit-offices",
    pattern: /^\/admin\/transit-offices/,
    requirement: {
      transitOfficeUser: true,
      api: { kind: "policy", policy: "OtModule", source: "services/core-api/src/Flit.Api/Endpoints/AdminTransitOfficesEndpoints.cs" },
    },
  },
  // HU #11228 — la consola de compañías de un AdminCompany (la página lo lleva a su tenant).
  {
    id: "admin-companies",
    pattern: /^\/admin\/companies/,
    requirement: { rolesAny: [ADMIN_COMPANY_ROLE], api: { kind: "policy", policy: "AdminCompany", source: "services/core-api/src/Flit.Api/Endpoints/AdminCompaniesEndpoints.cs" } },
  },
  // Feature #12201 — por permiso, no por rol.
  {
    id: "admin-generacion-documental",
    pattern: /^\/admin\/generacion-documental/,
    // Sin `rbacModule`: el borde no tiene el catálogo de módulos, solo los claims.
    requirement: {
      permissionsAny: MODULE_ACCESS["admin-generacion-documental"].permissionsAny,
      api: MODULE_ACCESS["admin-generacion-documental"].api,
    },
  },
  // Feature #12276 — único submódulo de Plataforma que no es solo del SuperAdmin.
  {
    id: "admin-confirmacion-runt",
    pattern: /^\/admin\/plataforma\/confirmacion-runt/,
    requirement: {
      permissionsAny: MODULE_ACCESS["admin-confirmacion-runt"].permissionsAny,
      api: MODULE_ACCESS["admin-confirmacion-runt"].api,
    },
  },
  // Todo lo demás de /admin (incluye Banners, HU #13439): solo SuperAdmin.
  { id: "admin", pattern: /^\/admin(\/|$)/, requirement: { superAdminOnly: true, api: ROUTE_API_ADMIN } },
  // /empresa/*: AdminCompany y SuperAdmin.
  {
    id: "empresa",
    pattern: /^\/empresa(\/|$)/,
    requirement: { rolesAny: [ADMIN_COMPANY_ROLE], api: { kind: "policy", policy: "AdminCompany", source: "services/core-api/src/Flit.Api/Endpoints/AdminCompaniesEndpoints.cs" } },
  },
];

/** Decodifica el pathname sin lanzar y en minúsculas: un valor malformado nunca abre un bypass. */
export function normalizePathname(pathname: string): string {
  let decoded = pathname;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    // se queda con el original
  }
  return decoded.toLowerCase();
}

/** Regla de la ruta, o `undefined` si el pathname no está protegido por la tabla. */
export function matchRouteRule(pathname: string): RouteAccessRule | undefined {
  const normalized = normalizePathname(pathname);
  return ROUTE_ACCESS.find((rule) => rule.pattern.test(normalized));
}

export interface RouteAccessDecision {
  allowed: boolean;
  /** Módulo de la regla que decidió (para `?modulo=`). */
  moduleId?: string;
}

/** Decide solo con claims. Una ruta que ninguna regla cubre no la gobierna esta tabla (pasa). */
export function resolveRouteAccess(claims: JwtPayload | null, pathname: string): RouteAccessDecision {
  const rule = matchRouteRule(pathname);
  if (!rule) return { allowed: true };
  return { allowed: canAccess({ claims, accessibleCodes: null }, rule.requirement), moduleId: rule.id };
}

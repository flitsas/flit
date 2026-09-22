import type { ManualNavSection } from "../types";

export const MANUAL_NAV_SECTIONS: readonly ManualNavSection[] = [
  { id: "introduccion", label: "Introducción", order: 0 },
  // Fuente principal de la plataforma: va antes que los perfiles para que se vea de entrada.
  { id: "normativa", label: "Normativa", order: 1 },
  { id: "gestor", label: "Gestor", order: 2 },
  { id: "ot", label: "Organismo de Tránsito", order: 3 },
  { id: "admin-company", label: "Administración de compañía", order: 4 },
  { id: "superadmin", label: "Super Admin", order: 5 },
] as const;

export const MANUAL_HOME_SLUG = "0-introduccion/1-bienvenida";

/** Manual v2.1 (DR-FLIT v3): 4 perfiles + Normativa (fuente principal), 40 artículos, sugerencia contextual y filtro por rol. */
export const MANUAL_VERSION = "2.1.0";
export const MANUAL_VERSION_DATE = "Septiembre 2026";

/**
 * HU-G — artículo que DR. FLIT sugiere según el módulo en el que está el usuario.
 *
 * Dos mapas porque los ids colisionan: la SPA tiene `usuarios` y `reportes` (Gestor) y el hub OT
 * tiene pestañas `usuarios` y `reportes` (Organismo) con artículos distintos. Un módulo sin
 * artículo no aparece (la sugerencia no se ofrece); un test comprueba que cada slug existe.
 */

/** Módulos de la SPA (`?m=…`, `ModuleId` de `Shell.tsx`) → slug. */
export const MANUAL_MODULE_ARTICLES: Readonly<Record<string, string>> = {
  dashboard: "1-gestor/1-inicio",
  tramites: "1-gestor/5-seguimiento",
  validaciones: "1-gestor/8-identidad",
  "historial-placa": "1-gestor/9-historial-placa",
  reportes: "1-gestor/11-reportes",
  "reportes-detallados": "1-gestor/11-reportes",
  usuarios: "1-gestor/12-usuarios",
  ayuda: "0-introduccion/1-bienvenida",
  // SuperAdmin (módulos SPA)
  rbac: "4-superadmin/5-rbac-y-auditoria",
  auditoria: "4-superadmin/5-rbac-y-auditoria",
  "log-qx": "4-superadmin/4-integraciones-y-procesos",
  ict: "4-superadmin/4-integraciones-y-procesos",
  "ict-logs": "4-superadmin/4-integraciones-y-procesos",
  "ict-reportes": "4-superadmin/4-integraciones-y-procesos",
  "ict-trazabilidad": "4-superadmin/4-integraciones-y-procesos",
};

/** Pestañas del hub OT (`segment` de `/admin/transit-offices/{id}/{segment}`) → slug. */
export const MANUAL_OT_TAB_ARTICLES: Readonly<Record<string, string>> = {
  "client-procedures": "2-ot/1-tramites-bandeja",
  "plate-ranges": "2-ot/2-preasignacion",
  reportes: "2-ot/3-reportes",
  usuarios: "2-ot/4-usuarios",
  rules: "2-ot/5-reglas",
  documents: "2-ot/6-documentos",
  requirements: "2-ot/7-requisitos",
  "revocation-requests": "2-ot/9-revocatorias",
  mandatos: "2-ot/10-mandatos",
  "imprint-validation": "2-ot/11-validar-impronta",
  configuracion: "2-ot/12-configuracion",
};

/** Rutas de página (fuera de `?m=`) con artículo propio; se evalúan por prefijo, en orden. */
export const MANUAL_PATH_ARTICLES: readonly { prefix: string; slug: string }[] = [
  { prefix: "/tramites/revocatorias", slug: "1-gestor/10-revocatorias" },
  { prefix: "/admin/generacion-documental", slug: "3-admin-company/5-generacion-documental" },
  // SuperAdmin (rutas). `/admin/transit-offices/{id}/…` lo resuelve antes el hub OT; aquí solo el catálogo.
  { prefix: "/admin/companies", slug: "4-superadmin/1-companias-y-organismos" },
  { prefix: "/admin/transit-offices", slug: "4-superadmin/1-companias-y-organismos" },
  { prefix: "/admin/causales-rechazo", slug: "4-superadmin/1-companias-y-organismos" },
  { prefix: "/admin/documents", slug: "4-superadmin/2-documental-e-improntas" },
  { prefix: "/admin/improntas", slug: "4-superadmin/2-documental-e-improntas" },
  { prefix: "/admin/plataforma", slug: "4-superadmin/3-plataforma" },
  { prefix: "/admin/banners", slug: "4-superadmin/3-plataforma" },
  { prefix: "/admin/quipux", slug: "4-superadmin/4-integraciones-y-procesos" },
  { prefix: "/admin/jobs", slug: "4-superadmin/4-integraciones-y-procesos" },
  { prefix: "/admin/migracion", slug: "4-superadmin/4-integraciones-y-procesos" },
  { prefix: "/admin/rbac", slug: "4-superadmin/5-rbac-y-auditoria" },
  { prefix: "/tramites/nuevo", slug: "1-gestor/2-crear-tramite" },
  // Sin barra final: cubre `/tramites` (listado) y `/tramites/{id}` (detalle). Las rutas más
  // específicas de arriba van antes porque el prefijo se evalúa en orden.
  { prefix: "/tramites", slug: "1-gestor/5-seguimiento" },
];

/**
 * Rutas con artículo que depende del SUFIJO, no del prefijo: la ficha de compañía
 * (`/admin/companies/{id}`) es la consola; la misma ruta con `/children` es la red.
 */
export const MANUAL_PATH_SUFFIX_ARTICLES: readonly { prefix: string; suffix: string; slug: string }[] = [
  { prefix: "/admin/companies/", suffix: "/children", slug: "3-admin-company/3-red-de-clientes" },
  { prefix: "/admin/companies/", suffix: "", slug: "3-admin-company/1-consola" },
];

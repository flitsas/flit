// HU #13649 (Feature #13102, ADR-0072): la tabla única de requisitos de acceso decide el menú, y el menú no ofrece lo
// que la API rechazaría.
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { buildDock } from "@flit/shell/nav";
import { tramitesNav } from "@/components/atom/dock/tramitesNav";
import { COPY } from "@/lib/copy/copy-catalog";
import {
  MODULE_ACCESS,
  canAccess,
  canAccessModule,
  moduleRequirement,
  navGatingFor,
  OT_HUB_TAB_PERMISSION,
  OT_PERMISSIONS,
  otHubNavGating,
  resolveRouteAccess,
  type OtHubDelegableTab,
  type AccessRequirement,
  type ModuleAccessKey,
} from "../module-access";
import { ALL_MODULE_IDS, resolveNavigableModuleIds } from "../modules";

const claims = (over: Record<string, unknown> = {}) => ({ sub: "u1", roles: [], permissions: [], ...over });
const superAdmin = claims({ roles: [{ code: "SuperAdmin" }] });
const adminCompany = claims({ roles: [{ code: "AdminCompany" }] });

describe("canAccess — evaluación pura (deny-by-default)", () => {
  it("sin claims legibles niega todo, también lo que no exige nada", () => {
    expect(canAccess({ claims: null, accessibleCodes: ["reportes"] }, { rbacModule: "reportes", api: { kind: "authenticated", source: "x" } })).toBe(false);
    expect(canAccessModule({ claims: null, accessibleCodes: null }, "dashboard")).toBe(false);
  });

  it("un módulo sin entrada en la tabla se niega (también al SuperAdmin)", () => {
    expect(moduleRequirement("modulo-inventado")).toBeUndefined();
    expect(canAccessModule({ claims: superAdmin, accessibleCodes: [] }, "modulo-inventado")).toBe(false);
    expect(moduleRequirement("constructor")).toBeUndefined();
  });

  it("superAdminOnly: el permiso no basta, ni siquiera con el slug en el token", () => {
    const req: AccessRequirement = { superAdminOnly: true, api: { kind: "policy", policy: "SuperAdmin", source: "x" } };
    expect(canAccess({ claims: claims({ permissions: ["banners.manage"] }), accessibleCodes: [] }, req)).toBe(false);
    expect(canAccess({ claims: adminCompany, accessibleCodes: [] }, req)).toBe(false);
    expect(canAccess({ claims: superAdmin, accessibleCodes: [] }, req)).toBe(true);
  });

  it("permissionsAny: basta uno de los slugs, sin distinguir mayúsculas", () => {
    const req: AccessRequirement = { permissionsAny: ["a.read", "b.read"], api: { kind: "permission", slug: "a.read", source: "x" } };
    expect(canAccess({ claims: claims({ permissions: ["B.READ"] }), accessibleCodes: null }, req)).toBe(true);
    expect(canAccess({ claims: claims({ permissions: ["c.read"] }), accessibleCodes: null }, req)).toBe(false);
  });

  it("el módulo RBAC se exige también al SuperAdmin (la API le entrega solo los módulos activos)", () => {
    const req: AccessRequirement = { rbacModule: "reportes", api: { kind: "authenticated", source: "x" } };
    expect(canAccess({ claims: superAdmin, accessibleCodes: [] }, req)).toBe(false);
    expect(canAccess({ claims: superAdmin, accessibleCodes: ["reportes"] }, req)).toBe(true);
  });
});

describe("HU #13649 AC1 — sin el permiso efectivo de Reportes, Reportes no aparece", () => {
  const viewer = (codes: string[]) => ({ permissions: [] as string[], isSuperAdmin: false, modules: codes });
  const labels = (codes: string[]) =>
    buildDock(
      tramitesNav({ isOtUser: false, isOtAdmin: false, isAdminCompany: false, isGroupParent: false, tenantId: null, otTransitOfficeId: null }),
      viewer(codes),
      "/",
    ).flatMap((g) => g.items.map((i) => i.key));

  it("en el menú (dock) y en el catálogo de `?m=`", () => {
    // El catálogo `security/modules` ya sale del claim `permissions` efectivo (rol + allow − deny): sin el permiso no viene.
    expect(labels(["tramites"])).not.toContain("reportes");
    expect(resolveNavigableModuleIds({ claims: claims(), accessibleCodes: ["tramites"] })).not.toContain("reportes");
  });

  it("con el permiso efectivo aparece en los dos", () => {
    expect(labels(["tramites", "reportes"])).toContain("reportes");
    expect(resolveNavigableModuleIds({ claims: claims(), accessibleCodes: ["reportes"] })).toContain("reportes");
  });
});

describe("HU #13649 AC3 — permisos aún no cargados: el menú no muestra módulos protegidos", () => {
  it("con claims y catálogo sin cargar solo hay Inicio y Ayuda", () => {
    expect(resolveNavigableModuleIds({ claims: adminCompany, accessibleCodes: null })).toEqual(["dashboard", "ayuda"]);
  });

  it("con el catálogo cargado pero claims ilegibles tampoco se abre nada protegido", () => {
    expect(resolveNavigableModuleIds({ claims: null, accessibleCodes: ["tramites", "reportes", "usuarios"] })).toEqual(["dashboard", "ayuda"]);
  });

  it("el dock no pinta entradas de módulo mientras `modules` no existe", () => {
    const keys = buildDock(
      tramitesNav({ isOtUser: false, isOtAdmin: false, isAdminCompany: false, isGroupParent: false, tenantId: null, otTransitOfficeId: null }),
      { permissions: [], isSuperAdmin: false },
      "/",
    ).flatMap((g) => g.items.map((i) => i.key));
    expect(keys).toEqual([]);
  });
});

describe("HU #13649 AC2 — un test por módulo: menú y API coinciden", () => {
  const entries = Object.entries(MODULE_ACCESS) as [ModuleAccessKey, AccessRequirement][];
  const repoRoot = resolve(__dirname, "../../../..");

  it("la tabla cubre todos los ids navegables de la SPA", () => {
    for (const id of ALL_MODULE_IDS) expect(moduleRequirement(id), `falta «${id}» en MODULE_ACCESS`).toBeDefined();
  });

  describe.each(entries)("módulo «%s»", (key, req) => {
    const api = req.api;

    it("el requisito del menú es al menos tan estricto como el de la API", () => {
      if (api.kind === "permission") {
        // Permiso de la API ⇒ el menú exige ese mismo permiso o el módulo RBAC que lo contiene.
        const porPermiso = req.permissionsAny?.map((p) => p.toLowerCase()).includes(api.slug.toLowerCase()) ?? false;
        const porModulo = Boolean(req.rbacModule) && api.slug.toLowerCase().startsWith(`${req.rbacModule}.`);
        expect(porPermiso || porModulo, `«${key}»: la API exige «${api.slug}» y el menú no`).toBe(true);
      } else if (api.kind === "policy" && api.policy === "SuperAdmin") {
        expect(req.superAdminOnly, `«${key}»: la API exige SuperAdmin y el menú no`).toBe(true);
      } else if (api.kind === "policy" && api.policy === "AdminCompany") {
        expect(req.rolesAny, `«${key}»: la API exige AdminCompany y el menú no`).toContain("AdminCompany");
      }
    });

    it("usuario sin lo que pide la API: el menú lo oculta; con lo que pide: lo muestra", () => {
      const rolesNada = claims();
      const sin = canAccess({ claims: rolesNada, accessibleCodes: [] }, req);
      const requiereAlgo = api.kind !== "authenticated";
      if (requiereAlgo) expect(sin).toBe(false);

      // Quien cumple exactamente el requisito entra.
      const cumple = claims({
        roles: req.superAdminOnly ? [{ code: "SuperAdmin" }] : (req.rolesAny ?? []).map((code) => ({ code })),
        permissions: [...(req.permissionsAny ?? [])],
        ...(req.transitOfficeUser ? { entity_type: "TRANSIT_OFFICE" } : {}),
      });
      expect(canAccess({ claims: cumple, accessibleCodes: req.rbacModule ? [req.rbacModule] : [] }, req)).toBe(true);
    });

    it("la política declarada sigue existiendo en el archivo de la API (si la API cambia y la tabla no, falla)", () => {
      const file = resolve(repoRoot, api.source);
      expect(existsSync(file), `no existe ${api.source}`).toBe(true);
      const text = readFileSync(file, "utf8");
      if (api.kind === "permission") expect(text).toContain(`"${api.slug}"`);
      if (api.kind === "policy" && api.policy === "SuperAdmin") expect(text).toMatch(/SuperAdminPolicy|RequireSuperAdmin/);
      if (api.kind === "policy" && api.policy === "AdminCompany") expect(text).toContain("AdminCompanyPolicy");
      if (api.kind === "policy" && api.policy === "OtAdminOrSuperAdmin") expect(text).toContain("OtAdminOrSuperAdminPolicy");
      if (api.kind === "policy" && api.policy === "OtModule") expect(text).toContain("OtModulePolicy");
    });
  });
});

describe("navGatingFor — el dock lee el mismo requisito", () => {
  it("traduce la tabla a los campos de NavItem", () => {
    expect(navGatingFor("reportes")).toEqual({ module: "reportes" });
    expect(navGatingFor("log-qx")).toEqual({ permission: ["logqx.read"] });
    expect(navGatingFor("rbac")).toEqual({ superAdminOnly: true });
    expect(navGatingFor("admin-generacion-documental")).toEqual({
      module: "generacion-documental",
      permission: ["generacion-documental.read"],
    });
    expect(navGatingFor("dashboard")).toEqual({});
  });
});

/** Etiquetas del dock de un usuario OT (organismo «ot-1») con los permisos dados. */
const otDockLabels = (isOtAdmin: boolean, permissions: string[]) =>
  buildDock(
    tramitesNav({ isOtUser: true, isOtAdmin, isAdminCompany: false, isGroupParent: false, tenantId: null, otTransitOfficeId: "ot-1" }),
    { permissions, isSuperAdmin: false },
    "/",
  ).flatMap((g) => g.items.flatMap((i) => [i.label, ...(i.children ?? []).map((c) => c.label)]));

describe("HU #13639 (Anexo A §1.4) — Documentos del organismo se delega con ot.documentos.manage", () => {
  const otUser = (permissions: string[], roles: { code: string }[] = [{ code: "gestor_tramites_ot" }]) =>
    claims({ roles, permissions, entity_type: "TRANSIT_OFFICE" });

  it("la ruta abre para el ot_admin y para el usuario OT con el permiso delegado, no para el resto", () => {
    expect(resolveRouteAccess(otUser([], [{ code: "ot_admin" }]), "/admin/transit-offices/x/documents").allowed).toBe(true);
    expect(resolveRouteAccess(otUser(["ot.documentos.manage"]), "/admin/transit-offices/x/documents").allowed).toBe(true);
    expect(resolveRouteAccess(otUser(["ot.tramites.manage"]), "/admin/transit-offices/x/documents").allowed).toBe(false);
  });

  it("el dock muestra Documentos con el permiso delegado; Usuarios no viene con él", () => {
    expect(otDockLabels(false, ["ot.documentos.manage"])).toContain("Documentos");
    expect(otDockLabels(false, ["ot.documentos.manage"])).not.toContain(COPY.B21Usuarios);
    expect(otDockLabels(false, [])).not.toContain("Documentos");
  });
});

describe("HU #13655 — las pestañas del organismo siguen el permiso OT de cada una (dock y ruta, misma tabla)", () => {
  const otUser = (permissions: string[], roles: { code: string }[] = [{ code: "gestor_tramites_ot" }]) =>
    claims({ roles, permissions, entity_type: "TRANSIT_OFFICE" });
  const tabs = Object.entries(OT_HUB_TAB_PERMISSION) as [OtHubDelegableTab, string][];

  it.each(tabs)("«%s»: el Gestor OT entra con %s y sin él recibe acceso denegado del módulo", (tab, slug) => {
    const path = `/admin/transit-offices/x/${tab}`;
    expect(resolveRouteAccess(otUser([slug]), path)).toEqual({ allowed: true, moduleId: `admin-transit-offices-${tab}` });
    expect(resolveRouteAccess(otUser([]), path)).toEqual({ allowed: false, moduleId: `admin-transit-offices-${tab}` });
    expect(resolveRouteAccess(otUser([], [{ code: "ot_admin" }]), path).allowed).toBe(true);
    expect(resolveRouteAccess(superAdmin, path).allowed).toBe(true);
    // El permiso OT no abre la ruta a quien no es de un organismo.
    expect(resolveRouteAccess(claims({ roles: [{ code: "Radicador" }], permissions: [slug] }), path).allowed).toBe(false);
  });

  it("los slugs de la tabla existen en el mapa de permisos OT de la API", () => {
    const text = readFileSync(resolve(__dirname, "../../../../services/core-api/src/Flit.Api/Authorization/OtPermissionMap.cs"), "utf8");
    for (const slug of Object.values(OT_PERMISSIONS)) expect(text).toContain(`"${slug}"`);
  });

  it("Reglas, Requisitos y Configuración siguen siendo solo del SuperAdmin, aunque el rol OT tenga todos los permisos", () => {
    for (const tab of ["rules", "requirements", "configuracion"]) {
      expect(resolveRouteAccess(otUser(Object.values(OT_PERMISSIONS)), `/admin/transit-offices/x/${tab}`).allowed).toBe(false);
    }
  });

  it("el Inicio y el listado del organismo siguen abiertos a cualquier rol OT", () => {
    expect(resolveRouteAccess(otUser([]), "/admin/transit-offices/x").allowed).toBe(true);
    expect(resolveRouteAccess(otUser([]), "/admin/transit-offices").allowed).toBe(true);
  });

  it("dock: el ot_admin ve todas las pestañas sin permisos OT en el token", () => {
    const labels = otDockLabels(true, []);
    for (const name of [COPY.B21Tramites, "Documentos", COPY.B21Usuarios, COPY.B21Reportes, "Mandatos", "Validar impronta"]) {
      expect(labels).toContain(name);
    }
  });

  it("dock: el Gestor OT ve solo las pestañas de sus permisos", () => {
    const labels = otDockLabels(false, ["ot.tramites.manage", "ot.reportes.manage"]);
    expect(labels).toEqual(expect.arrayContaining([COPY.B21Tramites, COPY.B21Reportes]));
    for (const name of ["Documentos", COPY.B21Usuarios, "Mandatos", "Validar impronta"]) expect(labels).not.toContain(name);
    expect(otDockLabels(false, ["ot.mandatos.manage", "ot.improntas.manage", "ot.usuarios.manage"])).toEqual(
      expect.arrayContaining(["Mandatos", "Validar impronta", COPY.B21Usuarios]),
    );
  });

  it("otHubNavGating: sin requisito para el ot_admin; el permiso de la pestaña para los demás", () => {
    expect(otHubNavGating("documents", true)).toEqual({});
    expect(otHubNavGating("documents", false)).toEqual({ permission: ["ot.documentos.manage"] });
    expect(otHubNavGating("imprint-validation", false)).toEqual({ permission: ["ot.improntas.manage"] });
  });
});

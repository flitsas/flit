import { describe, expect, it } from "vitest";
import { buildDock, type NavViewer } from "@flit/shell/nav";
import {
  DOCK_GROUP_ICON,
  DOCK_GROUP_LABEL,
  DOCK_GROUP_ORDER,
  DOCK_GROUP_SIDE,
  DOCK_ITEM_GROUP,
} from "../dockGroups";
import { tramitesNav, type TramitesNavContext } from "../tramitesNav";
import { OT_ADM_DOCK } from "@/components/admin/transit-offices/ot-nav";

// B-13 (HU #12989) — el catálogo de Trámites pasado por el mismo `buildDock` que usa la barra de la suite. Antes estas
// pruebas armaban entradas a mano con `buildDockGroups`; ahora prueban el catálogo real.

const base: TramitesNavContext = {
  isOtUser: false,
  isOtAdmin: false,
  isAdminCompany: false,
  isGroupParent: false,
  tenantId: null,
  otTransitOfficeId: null,
};
const viewer = (over: Partial<NavViewer> = {}): NavViewer => ({ permissions: [], isSuperAdmin: false, modules: [], ...over });
const dock = (ctx: Partial<TramitesNavContext>, v: Partial<NavViewer>, pathname = "/", search = "") =>
  buildDock(tramitesNav({ ...base, ...ctx }), viewer(v), pathname, search);

describe("catálogo de Trámites", () => {
  it("expone Trámites e Identidad como píldoras de una entrada (sin submenú Operación)", () => {
    const groups = dock({}, { modules: ["tramites", "validaciones", "reportes"] });
    expect(groups.map((g) => g.label)).toEqual(["Trámites", "Identidad", "Reportes"]);
    expect(groups[0].items.map((i) => i.label)).toEqual(["Trámites"]);
    expect(groups[1].items.map((i) => i.label)).toEqual(["Identidad"]);
  });

  it("no hay entrada de dashboard: el botón central es el inicio", () => {
    const keys = dock({}, { modules: ["dashboard", "tramites"] }).flatMap((g) => g.items.map((i) => i.key));
    expect(keys).toEqual(["tramites"]);
  });

  // HU #12850, #12856 — Admin OT: Administración agrupa Documentos, Mandatos y Validar impronta; sin Reglas,
  // Requisitos, Configuración ni Preasignación.
  it("Admin OT: pestañas del organismo repartidas en Trámites, Reportes, Usuarios y Administración", () => {
    const groups = dock(
      { isOtUser: true, isOtAdmin: true, otTransitOfficeId: "ot-1" },
      { roles: ["ot_admin"], modules: ["tramites", "reportes", "usuarios"] },
    );
    expect(groups.map((g) => g.label)).toEqual(["Trámites", "Reportes", "Usuarios", "Administración"]);
    expect(groups.find((g) => g.id === "administracion")?.items.map((i) => i.label)).toEqual([
      "Documentos",
      "Mandatos",
      "Validar impronta",
    ]);
    // Los módulos homónimos de la SPA se omiten: el único Trámites es el del organismo.
    expect(groups[0].items.map((i) => i.href)).toEqual(["/admin/transit-offices/ot-1/client-procedures"]);
  });

  it("usuario OT sin organismo resuelto todavía: sus pestañas esperan (no hay a dónde llevarlas)", () => {
    const keys = dock({ isOtUser: true, isOtAdmin: true }, { modules: ["tramites"] }).flatMap((g) => g.items.map((i) => i.key));
    expect(keys).not.toContain(OT_ADM_DOCK.tramites);
    expect(keys).not.toContain("tramites");
  });

  it("HU12856 AC1 — DOCK_ITEM_GROUP no conserva las claves de Reglas/Requisitos/Configuración", () => {
    expect(Object.keys(DOCK_ITEM_GROUP)).not.toContain(OT_ADM_DOCK.rules);
    expect(Object.keys(DOCK_ITEM_GROUP)).not.toContain(OT_ADM_DOCK.requirements);
    expect(Object.keys(DOCK_ITEM_GROUP)).not.toContain(OT_ADM_DOCK.configuracion);
  });

  it("toda entrada del catálogo tiene grupo: ninguna se descarta en silencio", () => {
    const everything = tramitesNav({ ...base, isOtUser: true, isOtAdmin: true, isAdminCompany: true, isGroupParent: true, tenantId: "t", otTransitOfficeId: "o" });
    for (const item of everything.items) expect(DOCK_ITEM_GROUP[item.key], item.key).toBe(item.section);
  });

  // HU12850 AC3 — `preasignacion` no aparece en ninguno de los mapas.
  it("HU12850 AC3 — ningún mapa del dock conserva la clave 'preasignacion'", () => {
    expect(DOCK_GROUP_ORDER).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_SIDE)).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_LABEL)).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_ICON)).not.toContain("preasignacion");
    expect(Object.values(DOCK_ITEM_GROUP)).not.toContain("preasignacion");
    expect(Object.keys(OT_ADM_DOCK)).not.toContain("preasignacion");
  });

  it("SuperAdmin: Administradores en el orden de siempre, con Plataforma anidada", () => {
    const groups = dock({}, { isSuperAdmin: true });
    const admin = groups.find((g) => g.id === "administradores")!;
    expect(admin.items.map((i) => i.label)).toEqual([
      "Compañías",
      "Tránsito",
      "Documental",
      "Improntas",
      "Quipux",
      "Procesos periódicos",
      "RBAC Admin",
      "Auditoría",
      "Plataforma",
      "Generación documental",
    ]);
    const plataforma = admin.items.find((i) => i.key === "admin-plataforma");
    expect(plataforma?.children?.map((c) => c.label)).toEqual([
      "Tipos de trámites",
      "Confirmación RUNT",
      "Mandatos",
      "FUR",
      "Notificaciones",
      "Descarga masiva",
      "Banners",
    ]);
  });

  // HU #13420 AC5 — la entrada de los parámetros del motor de descarga masiva es solo del SuperAdmin.
  it("HU13420 AC5 — «Descarga masiva» solo con SuperAdmin: apunta a su pantalla y no aparece sin el rol", () => {
    const keys = (v: Partial<NavViewer>, ctx: Partial<TramitesNavContext> = {}) =>
      dock(ctx, v).flatMap((g) => g.items.flatMap((i) => [i.key, ...(i.children?.map((c) => c.key) ?? [])]));
    const plataforma = dock({}, { isSuperAdmin: true })
      .find((g) => g.id === "administradores")
      ?.items.find((i) => i.key === "admin-plataforma");
    expect(plataforma?.children?.find((c) => c.key === "admin-descarga-masiva")?.href).toBe(
      "/admin/plataforma/descarga-masiva",
    );
    expect(keys({ roles: ["AdminCompany"], permissions: ["banners.manage", "consolidados.lote.crear"] }, { isAdminCompany: true })).not.toContain(
      "admin-descarga-masiva",
    );
    expect(keys({ roles: ["ot_admin"], modules: ["tramites"] }, { isOtUser: true, isOtAdmin: true, otTransitOfficeId: "ot-1" })).not.toContain(
      "admin-descarga-masiva",
    );
  });

  it("Banners por permiso: un AdminCompany con banners.manage ve Plataforma solo con Banners", () => {
    const groups = dock({ isAdminCompany: true }, { roles: ["AdminCompany"], permissions: ["banners.manage"] });
    const plataforma = groups.find((g) => g.id === "administradores")?.items.find((i) => i.key === "admin-plataforma");
    expect(plataforma?.children?.map((c) => c.label)).toEqual(["Banners"]);
  });

  it("HU #12723 — cada agrupador declara lado izquierdo o derecho del inicio", () => {
    expect(DOCK_GROUP_SIDE).toEqual({
      tramites: "left",
      identidad: "left",
      reportes: "left",
      usuarios: "right",
      administracion: "right",
      administradores: "right",
      integraciones: "right",
    });
    expect(dock({}, { modules: ["tramites", "usuarios"] }).map((g) => g.side)).toEqual(["left", "right"]);
  });

  it("HU #12723 — Ayuda no está en el dock", () => {
    expect(DOCK_GROUP_ORDER).not.toContain("ayuda");
    const keys = dock({}, { isSuperAdmin: true }).flatMap((g) => g.items.map((i) => i.key));
    expect(keys).not.toContain("ayuda");
  });

  it("Integraciones agrupa Log QX e ICT, con Log ICT, Trazabilidad ICT y Reportes ICT bajo ICT", () => {
    const groups = dock({}, { permissions: ["logqx.read", "ict.logs.read"] });
    expect(groups.map((g) => g.label)).toEqual(["Integraciones"]);
    expect(groups[0].items.map((i) => i.label)).toEqual(["Log QX", "ICT"]);
    expect(groups[0].items[1].children?.map((c) => c.label)).toEqual(["Log ICT", "Trazabilidad ICT", "Reportes ICT"]);
  });

  it("la entrada activa sale de la URL: /?m=ict-logs marca Log ICT y su grupo", () => {
    const [integraciones] = dock({}, { permissions: ["ict.logs.read"] }, "/", "m=ict-logs");
    expect(integraciones.active).toBe(true);
    expect(integraciones.items[0].children?.find((c) => c.active)?.key).toBe("ict-logs");
  });
});

import { describe, expect, it } from "vitest";
import { buildDock, type NavViewer } from "@flit/shell/nav";
import { tramitesNav, type TramitesNavContext } from "../tramitesNav";

// HU #14074 AC1 — «Persona Natural» cuelga de Plataforma y solo la ve el superadministrador. Lección del dock: una
// entrada nueva sin su clave en DOCK_ITEM_GROUP se descarta en silencio; los hijos de Plataforma heredan el grupo del
// padre (`admin-plataforma`), así que aquí se comprueba que la entrada de verdad llega al dock.

const base: TramitesNavContext = {
  isOtUser: false,
  isOtAdmin: false,
  isAdminCompany: false,
  isGroupParent: false,
  tenantId: null,
  otTransitOfficeId: null,
};
const viewer = (over: Partial<NavViewer> = {}): NavViewer => ({ permissions: [], isSuperAdmin: false, modules: [], ...over });
const keys = (ctx: Partial<TramitesNavContext>, v: Partial<NavViewer>) =>
  buildDock(tramitesNav({ ...base, ...ctx }), viewer(v), "/", "").flatMap((g) =>
    g.items.flatMap((i) => [i.key, ...(i.children?.map((c) => c.key) ?? [])]),
  );

describe("HU14074 AC1 — entrada «Persona Natural» de Plataforma", () => {
  it("el SuperAdmin la ve dentro de Plataforma y apunta a su pantalla", () => {
    const plataforma = buildDock(tramitesNav(base), viewer({ isSuperAdmin: true }), "/", "")
      .find((g) => g.id === "administradores")
      ?.items.find((i) => i.key === "admin-plataforma");
    const entrada = plataforma?.children?.find((c) => c.key === "admin-persona-natural");
    expect(entrada?.label).toBe("Persona Natural");
    expect(entrada?.href).toBe("/admin/plataforma/persona-natural");
  });

  it("quien tiene el permiso pero no es SuperAdmin no la ve (la API exige el rol y el permiso)", () => {
    expect(
      keys({ isAdminCompany: true }, { roles: ["AdminCompany"], permissions: ["platform.persona_natural.manage"] }),
    ).not.toContain("admin-persona-natural");
  });

  it("ni el administrador de un organismo de tránsito ni un usuario común la ven", () => {
    expect(
      keys({ isOtUser: true, isOtAdmin: true, otTransitOfficeId: "ot-1" }, { roles: ["ot_admin"], modules: ["tramites"] }),
    ).not.toContain("admin-persona-natural");
    expect(keys({}, { modules: ["tramites", "usuarios"] })).not.toContain("admin-persona-natural");
  });
});

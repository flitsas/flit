import { describe, expect, it } from "vitest";
import { OT_HUB_TAB_PERMISSION } from "@/lib/nav/module-access";
import { OT_HUB_TABS, isOtHubSegmentActive, otHubModulePath } from "../../ot-nav";

describe("HU #14377 — pestaña Portal Persona Natural en el hub", () => {
  it("está en las pestañas del hub con su ruta y se marca activa en ella", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "portal-persona-natural");
    expect(tab).toEqual({ id: "portal-persona-natural", label: "Portal Persona Natural", segment: "portal-persona-natural" });
    expect(otHubModulePath("ot-1", "portal-persona-natural")).toBe("/admin/transit-offices/ot-1/portal-persona-natural");
    expect(isOtHubSegmentActive("/admin/transit-offices/ot-1/portal-persona-natural", "portal-persona-natural")).toBe(true);
  });

  it("es por rol (SuperAdmin y ot_admin): no se delega con un permiso OT nuevo", () => {
    expect(Object.keys(OT_HUB_TAB_PERMISSION)).not.toContain("portal-persona-natural");
  });
});

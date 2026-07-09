import { describe, expect, it } from "vitest";
import { foldOtSearch, matchesOtOfficeSearch, OT_HUB_TABS, otHubModulePath } from "../ot-nav";

describe("ot-nav — refactor adminOT", () => {
  it("incluye el tab 'usuarios' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "usuarios");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Usuarios");
    expect(tab?.segment).toBe("usuarios");
  });

  it("otHubModulePath arma la ruta del tab usuarios", () => {
    expect(otHubModulePath("ot-1", "usuarios")).toBe("/admin/transit-offices/ot-1/usuarios");
  });
});

describe("ot-nav — HU #10236", () => {
  it("foldOtSearch ignora tildes", () => {
    expect(foldOtSearch("Bogotá")).toBe("bogota");
  });

  it("matchesOtOfficeSearch por nombre y codigo", () => {
    const office = { name: "Secretaría Bogotá", code: "11001" };
    expect(matchesOtOfficeSearch(office, "bogota")).toBe(true);
    expect(matchesOtOfficeSearch(office, "11001")).toBe(true);
    expect(matchesOtOfficeSearch(office, "medellin")).toBe(false);
  });
});

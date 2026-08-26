import { describe, expect, it, beforeEach } from "vitest";
import {
  extractTransitOfficeIdFromPath,
  foldOtSearch,
  isOtHubSegmentActive,
  matchesOtOfficeSearch,
  OT_HUB_TABS,
  otHubModulePath,
  resolveOtHubHref,
} from "../ot-nav";

describe("ot-nav — refactor adminOT", () => {
  it("incluye el tab 'usuarios' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "usuarios");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Usuarios");
    expect(tab?.segment).toBe("usuarios");
  });

  it("incluye reportes y labels cortos de Trámites / Preasignación", () => {
    expect(OT_HUB_TABS.find((t) => t.id === "client-procedures")?.label).toBe("Trámites");
    expect(OT_HUB_TABS.find((t) => t.id === "plate-ranges")?.label).toBe("Preasignación");
    expect(OT_HUB_TABS.find((t) => t.id === "reportes")?.label).toBe("Reportes");
  });

  it("otHubModulePath arma la ruta del tab usuarios", () => {
    expect(otHubModulePath("ot-1", "usuarios")).toBe("/admin/transit-offices/ot-1/usuarios");
  });

  // "Trámites" y "Webhooks" (ids legacy) salieron de la consola: la ruta sigue viva por URL.
  it.each(["tramites", "webhooks"])("no ofrece la pestaña legacy '%s'", (id) => {
    expect(OT_HUB_TABS.some((t) => t.id === id)).toBe(false);
  });

  it("el primer módulo visible es 'client-procedures' (Trámites)", () => {
    expect(OT_HUB_TABS[0].id).toBe("client-procedures");
  });
});

describe("ot-nav — resolución de rutas dock", () => {
  beforeEach(() => {
    try {
      window.sessionStorage.clear();
    } catch {
      /* jsdom */
    }
  });

  it("extractTransitOfficeIdFromPath lee el id del hub", () => {
    expect(extractTransitOfficeIdFromPath("/admin/transit-offices/ot-99/rules")).toBe("ot-99");
    expect(extractTransitOfficeIdFromPath("/admin/transit-offices")).toBeNull();
  });

  it("isOtHubSegmentActive solo cuando el segmento coincide", () => {
    expect(isOtHubSegmentActive("/admin/transit-offices/ot-1/rules", "rules")).toBe(true);
    expect(isOtHubSegmentActive("/admin/transit-offices/ot-1/rules", "documents")).toBe(false);
  });

  it("resolveOtHubHref usa el id de la ruta cuando existe", async () => {
    const href = await resolveOtHubHref(
      "documents",
      "/admin/transit-offices/ot-7/rules",
      "superadmin",
      async () => "should-not-call",
    );
    expect(href).toBe("/admin/transit-offices/ot-7/documents");
  });

  it("resolveOtHubHref (SuperAdmin sin id) vuelve al listado", async () => {
    const href = await resolveOtHubHref("rules", "/admin/transit-offices", "superadmin", async () => "x");
    expect(href).toBe("/admin/transit-offices");
  });

  it("resolveOtHubHref (Admin OT sin id) usa el perfil", async () => {
    const href = await resolveOtHubHref("plate-ranges", "/", "ot_admin", async () => "ot-from-profile");
    expect(href).toBe("/admin/transit-offices/ot-from-profile/plate-ranges");
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

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
  it("incluye el tab 'mandatos' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "mandatos");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Mandatos");
  });

  it("incluye el tab 'imprint-validation' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "imprint-validation");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Validar impronta");
    expect(tab?.segment).toBe("imprint-validation");
  });

  // HU #12578 (Feature #12565, AC1) — vista dedicada "Revocatorias" del hub OT.
  it("incluye el tab 'revocation-requests' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "revocation-requests");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Revocatorias");
    expect(tab?.segment).toBe("revocation-requests");
  });

  // Pedido del usuario (2026-09-16) — modo Dashboard/QX, ventana de revocatoria (HU #12569) y
  // feature flags operativos solo vivían en una ruta legacy sin enlace en ningún menú.
  it("incluye el tab 'configuracion' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "configuracion");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Configuración");
    expect(tab?.segment).toBe("configuracion");
  });

  it("otHubModulePath arma la ruta del tab configuracion", () => {
    expect(otHubModulePath("ot-1", "configuracion")).toBe(
      "/admin/transit-offices/ot-1/configuracion",
    );
  });

  it("incluye el tab 'usuarios' en OT_HUB_TABS", () => {
    const tab = OT_HUB_TABS.find((t) => t.id === "usuarios");
    expect(tab).toBeDefined();
    expect(tab?.label).toBe("Usuarios");
    expect(tab?.segment).toBe("usuarios");
  });

  it("incluye reportes y labels cortos de Trámites", () => {
    expect(OT_HUB_TABS.find((t) => t.id === "client-procedures")?.label).toBe("Trámites");
    expect(OT_HUB_TABS.find((t) => t.id === "reportes")?.label).toBe("Reportes");
  });

  // HU12850 AC1/AC2 — Preasignación se retiró del hub: ni la pestaña ni la key del dock existen.
  // `id` se compara como string porque "plate-ranges" ya no es un miembro válido de OtHubTabId.
  it("HU12850 AC1 — ya no ofrece la pestaña 'plate-ranges' (Preasignación)", () => {
    expect(OT_HUB_TABS.some((t) => (t.id as string) === "plate-ranges")).toBe(false);
  });

  it("otHubModulePath arma la ruta del tab imprint-validation", () => {
    expect(otHubModulePath("ot-1", "imprint-validation")).toBe(
      "/admin/transit-offices/ot-1/imprint-validation",
    );
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
    const href = await resolveOtHubHref("documents", "/", "ot_admin", async () => "ot-from-profile");
    expect(href).toBe("/admin/transit-offices/ot-from-profile/documents");
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

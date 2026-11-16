import { describe, expect, it } from "vitest";
import { Box } from "lucide-react";
import { buildDock, type NavCatalog } from "../nav";
import { buildSearchResults, looksLikePlate, normalizePlate, sectionResults } from "../search";

// HU #14480 — AC1: el buscador solo ofrece lo que el rol ve en el dock.
const catalog: NavCatalog = {
  sections: [
    { id: "operacion", label: "Operación", icon: Box, side: "left" },
    { id: "admin", label: "Administradores", icon: Box, side: "right" },
  ],
  items: [
    { key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box },
    { key: "reportes", label: "Reportes", href: "/?m=reportes", section: "operacion", icon: Box, permission: "reportes.ver" },
    {
      key: "plataforma",
      label: "Plataforma",
      href: "",
      section: "admin",
      icon: Box,
      children: [{ key: "mandatos", label: "Mandatos", href: "/admin/mandatos", section: "admin", icon: Box, superAdminOnly: true }],
    },
  ],
};
const gestor = { permissions: [], isSuperAdmin: false };
const superAdmin = { permissions: [], isSuperAdmin: true };
const actions = [
  { key: "sin_firmas", label: "Trámites sin firmas", href: "/tramites?atajo=sin_firmas", requires: "tramites" },
  { key: "informe", label: "Informe anual", href: "/?m=reportes&x=1", requires: "reportes" },
];
const plate = { href: (p: string) => `/tramites?placa=${p}`, hint: "Buscar en Trámites", requires: "tramites" };

describe("buscador: secciones por permiso (AC1)", () => {
  it("un rol sin permiso no encuentra la sección ni la acción que dependen de ella", () => {
    const groups = buildDock(catalog, gestor, "/");
    const labels = buildSearchResults("", groups, { actions }).map((r) => r.label);
    expect(labels).toEqual(["Trámites", "Trámites sin firmas"]);
    expect(buildSearchResults("reportes", groups, { actions })).toEqual([]);
    expect(buildSearchResults("mandatos", groups)).toEqual([]);
  });

  it("con el permiso aparecen; los submenús se buscan por su nombre y muestran su ruta en el dock", () => {
    const groups = buildDock(catalog, superAdmin, "/");
    const r = buildSearchResults("mandatos", groups);
    expect(r).toEqual([expect.objectContaining({ group: "secciones", label: "Mandatos", href: "/admin/mandatos", description: "Administradores · Plataforma" })]);
    expect(buildSearchResults("informe", groups, { actions }).map((x) => x.href)).toEqual(["/?m=reportes&x=1"]);
  });

  it("un contenedor sin ruta no es un resultado; Inicio sí", () => {
    const groups = buildDock(catalog, superAdmin, "/");
    const labels = sectionResults(groups, { href: "/", label: "Inicio Trámites" }).map((r) => r.label);
    expect(labels).toEqual(["Inicio", "Trámites", "Reportes", "Mandatos"]);
  });

  it("busca sin tildes ni mayúsculas", () => {
    const groups = buildDock(catalog, gestor, "/");
    expect(buildSearchResults("TRAMITES", groups).map((r) => r.href)).toEqual(["/tramites"]);
  });
});

describe("buscador: placas (AC2)", () => {
  it("reconoce placa de carro y de moto, con espacios o guion", () => {
    expect(looksLikePlate("abc123")).toBe(true);
    expect(looksLikePlate("ABC 12D")).toBe(true);
    expect(looksLikePlate("abc-123")).toBe(true);
    expect(looksLikePlate("ABC1234")).toBe(false);
    expect(looksLikePlate("tramites")).toBe(false);
    expect(normalizePlate(" abc 12d ")).toBe("ABC12D");
  });

  it("una placa lleva al listado de Trámites con ?placa= y va primero", () => {
    const groups = buildDock(catalog, gestor, "/");
    const [first] = buildSearchResults("abc 123", groups, { plate });
    expect(first).toEqual(expect.objectContaining({ group: "placas", label: "ABC123", href: "/tramites?placa=ABC123" }));
  });

  it("sin la entrada de la que depende, no ofrece la placa", () => {
    const groups = buildDock(catalog, gestor, "/");
    expect(buildSearchResults("ABC123", groups, { plate: { ...plate, requires: "reportes" } })).toEqual([]);
  });
});

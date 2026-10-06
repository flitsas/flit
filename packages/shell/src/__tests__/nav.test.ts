import { describe, expect, it } from "vitest";
import { Box } from "lucide-react";
import { buildDock, type NavCatalog } from "../nav";

// B-10 (HU #12986) — Contratos A y B de la guía del dock.
const catalog: NavCatalog = {
  sections: [
    { id: "operacion", label: "Operación", icon: Box, side: "left" },
    { id: "reportes", label: "Reportes", icon: Box, side: "left" },
    { id: "admin", label: "Administración", icon: Box, side: "right" },
  ],
  items: [
    { key: "admin-usuarios", label: "Usuarios", href: "/admin/usuarios", section: "admin", icon: Box, permission: "usuarios.manage" },
    { key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box },
    { key: "tramites-nuevo", label: "Nuevo", href: "/tramites/nuevo", section: "operacion", icon: Box },
    { key: "rbac", label: "RBAC", href: "/admin/rbac", section: "admin", icon: Box, superAdminOnly: true },
    { key: "reportes", label: "Reportes", href: "/reportes", section: "reportes", icon: Box, permission: "reportes.read" },
  ],
};
const viewer = (permissions: string[] = [], isSuperAdmin = false) => ({ permissions, isSuperAdmin });

describe("buildDock", () => {
  it("el orden sale de las secciones, no de los ítems, y se descartan las secciones vacías", () => {
    const groups = buildDock(catalog, viewer(["usuarios.manage"]), "/");
    expect(groups.map((g) => g.id)).toEqual(["operacion", "admin"]);
  });

  it("filtra por permiso y deja lo exclusivo del SuperAdmin fuera para los demás", () => {
    const admin = buildDock(catalog, viewer(["usuarios.manage", "reportes.read"]), "/").find((g) => g.id === "admin")!;
    expect(admin.items.map((i) => i.key)).toEqual(["admin-usuarios"]);
  });

  it("filtra por rol cuando el ítem lo pide", () => {
    const withRole: NavCatalog = { sections: catalog.sections, items: [{ key: "empresa", label: "Empresa", href: "/empresa", section: "admin", icon: Box, roles: ["AdminCompany"] }] };
    expect(buildDock(withRole, { permissions: [], isSuperAdmin: false, roles: ["Radicador"] }, "/")).toEqual([]);
    expect(buildDock(withRole, { permissions: [], isSuperAdmin: false, roles: ["AdminCompany"] }, "/")).toHaveLength(1);
  });

  it("el SuperAdmin ve todo", () => {
    const groups = buildDock(catalog, viewer([], true), "/");
    expect(groups.flatMap((g) => g.items.map((i) => i.key))).toHaveLength(5);
  });

  it("la ruta activa es la del prefijo más largo y marca su grupo", () => {
    const groups = buildDock(catalog, viewer(), "/tramites/nuevo/paso-2");
    const operacion = groups.find((g) => g.id === "operacion")!;
    expect(operacion.active).toBe(true);
    expect(operacion.items.find((i) => i.active)?.key).toBe("tramites-nuevo");
  });

  it("una URL de otro producto nunca queda activa", () => {
    const external: NavCatalog = { sections: catalog.sections, items: [{ key: "x", label: "Hub", href: "https://dev.flitsas.online/", section: "operacion", icon: Box }] };
    expect(buildDock(external, viewer(), "/").flatMap((g) => g.items)[0].active).toBe(false);
  });

  // B-13 (HU #12989) — lo que Trámites necesita del contrato.
  it("una entrada con consulta solo está activa en esa ruta y con esos parámetros", () => {
    const spa: NavCatalog = {
      sections: catalog.sections,
      items: [
        { key: "reportes", label: "Reportes", href: "/?m=reportes", section: "reportes", icon: Box },
        { key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box },
      ],
    };
    const active = (path: string, search: string) =>
      buildDock(spa, viewer(), path, search).flatMap((g) => g.items).find((i) => i.active)?.key ?? null;
    expect(active("/", "?m=reportes")).toBe("reportes");
    expect(active("/", "?m=usuarios")).toBeNull();
    expect(active("/admin/companies", "?m=reportes")).toBeNull();
    expect(active("/tramites/abc", "")).toBe("tramites");
  });

  it("filtra por módulo RBAC y, sin la lista de módulos, no muestra lo que depende de uno", () => {
    const withModule: NavCatalog = {
      sections: catalog.sections,
      items: [{ key: "reportes", label: "Reportes", href: "/?m=reportes", section: "reportes", icon: Box, module: "reportes" }],
    };
    expect(buildDock(withModule, { permissions: [], isSuperAdmin: false, modules: ["tramites"] }, "/")).toEqual([]);
    expect(buildDock(withModule, { permissions: [], isSuperAdmin: false, modules: ["reportes"] }, "/")).toHaveLength(1);
    expect(buildDock(withModule, { permissions: [], isSuperAdmin: false }, "/")).toEqual([]);
  });

  it("con varios permisos basta uno", () => {
    const anyOf: NavCatalog = {
      sections: catalog.sections,
      items: [{ key: "runt", label: "RUNT", href: "/runt", section: "admin", icon: Box, permission: ["runt.manage", "runt.read"] }],
    };
    expect(buildDock(anyOf, viewer(["runt.read"]), "/")).toHaveLength(1);
    expect(buildDock(anyOf, viewer(["otro"]), "/")).toEqual([]);
  });

  it("un contenedor sin hijos visibles desaparece y nunca queda activo por sí mismo", () => {
    const nested: NavCatalog = {
      sections: catalog.sections,
      items: [
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
    expect(buildDock(nested, viewer(), "/")).toEqual([]);
    const [group] = buildDock(nested, viewer([], true), "/tramites");
    expect(group.active).toBe(false);
    expect(buildDock(nested, viewer([], true), "/admin/mandatos")[0].items[0].children?.[0].active).toBe(true);
  });
});

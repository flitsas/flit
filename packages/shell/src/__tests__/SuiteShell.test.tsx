import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { Box } from "lucide-react";
import { SuiteShell } from "../SuiteShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/tramites" }));
afterEach(cleanup);

const nav = {
  sections: [{ id: "operacion", label: "Operación", icon: Box, side: "left" as const }],
  items: [{ key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box }],
};
const user = { email: "ana@empresa.co", tenantName: "Empresa Uno", permissions: [], isSuperAdmin: false };

describe("SuiteShell", () => {
  it("dibuja marca, producto, cuenta y el dock del catálogo con la ruta activa", () => {
    render(<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user}>contenido</SuiteShell>);

    expect(screen.getByText("contenido")).toBeInTheDocument();
    expect(screen.getAllByText("Trámites").length).toBeGreaterThan(0);
    const dock = screen.getByRole("navigation", { name: "Navegación principal" });
    expect(within(dock).getByRole("link", { name: "Trámites" })).toHaveAttribute("aria-current", "page");
    fireEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.getByRole("link", { name: "Salir de la plataforma" })).toHaveAttribute("href", "/auth/logout");
  });

  it("la barra es la de siempre: rol, empresa, nombre, avatar con la inicial y el menú ⋮ (más producto y ▦)", () => {
    const ana = { ...user, displayName: "Ana Ruiz", roleLabel: "Admin de Compañía" };
    render(<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={ana}>x</SuiteShell>);

    const header = screen.getByRole("banner");
    expect(within(header).getByText("Admin de Compañía")).toBeInTheDocument();
    expect(within(header).getByText("Empresa Uno")).toBeInTheDocument();
    expect(within(header).getByText("Ana Ruiz")).toBeInTheDocument();
    expect(within(header).getByLabelText("Avatar")).toHaveTextContent("A");
    expect(within(header).getByText("Trámites")).toBeInTheDocument();
    expect(within(header).getByRole("button", { name: "Productos" })).toBeInTheDocument();
    expect(within(header).getByRole("button", { name: "Menú de usuario" })).toBeInTheDocument();
  });

  it("sin configurar nada, el menú de usuario trae las opciones de la suite y el rol sale de los roles", () => {
    const ana = { ...user, roles: ["AdminCompany"] };
    render(<SuiteShell productCode="comparendos" productName="Comparendos" nav={nav} user={ana} accountUrl="https://t.test">x</SuiteShell>);

    expect(within(screen.getByRole("banner")).getByText("Admin de Compañía")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.getByRole("link", { name: "Ayuda" })).toHaveAttribute("href", "https://t.test/manual");
    expect(screen.getByRole("link", { name: "Cambio de contraseña" })).toHaveAttribute("href", "https://t.test/profile/change-password");
    expect(screen.getByRole("link", { name: "Salir de la plataforma" })).toBeInTheDocument();
  });

  it("el menú de productos dice que no pudo cargarlos en vez de «no hay productos» si la carga falla", async () => {
    const loadApps = () => Promise.reject(new Error("401"));
    render(<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} loadApps={loadApps}>x</SuiteShell>);

    fireEvent.click(screen.getByRole("button", { name: "Productos" }));
    expect(await screen.findByText("No pudimos cargar los productos.")).toBeInTheDocument();
  });

  it("el menú de productos lista los productos y marca el actual", () => {
    const apps = [
      { code: "plataforma", name: "Plataforma", icon: "layout-grid", url: "https://dev.flitsas.online", current: false },
      { code: "tramites", name: "Trámites", icon: "file-text", url: "https://dev.tramites.flitsas.online", current: true },
    ];
    render(<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} apps={apps}>x</SuiteShell>);

    fireEvent.click(screen.getByRole("button", { name: "Productos" }));
    const panel = screen.getByRole("region", { name: "Productos" });
    expect(within(panel).getByRole("link", { name: "Ir al inicio" })).toHaveAttribute("href", "https://dev.flitsas.online/?inicio=1");
    expect(within(panel).getByRole("link", { name: /^Trámites/ })).toHaveAttribute("aria-current", "page");
  });

  it("el menú de productos dice qué se hace ahí, qué es cada producto y cuál está por llegar", () => {
    const apps = [
      { code: "plataforma", name: "Plataforma", icon: "layout-grid", url: "https://dev.flitsas.online", current: false },
      { code: "tramites", name: "Trámites", icon: "file-text", url: "https://dev.tramites.flitsas.online", current: true },
      { code: "comparendos", name: "Comparendos", icon: "ticket", url: "https://dev.flitsas.online/proximamente/comparendos", current: false, comingSoon: true },
    ];
    render(<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} apps={apps}>x</SuiteShell>);

    fireEvent.click(screen.getByRole("button", { name: "Productos" }));
    const panel = screen.getByRole("region", { name: "Productos" });
    expect(within(panel).getByText("¿En qué quieres trabajar hoy?")).toBeInTheDocument();
    expect(within(panel).getByRole("link", { name: /^Trámites.*Estás aquí/ })).toHaveTextContent("Matrículas, traspasos y trámites vehiculares");
    expect(within(panel).getByRole("link", { name: /^Comparendos.*Próximamente/ })).toHaveAttribute("href", "https://dev.flitsas.online/proximamente/comparendos");
    // La plataforma no es una fila de producto: es «Ir al inicio», al pie.
    expect(within(panel).queryByRole("link", { name: /^Plataforma/ })).toBeNull();
  });
});

describe("independencia de los productos", () => {
  it("@flit/shell no importa código de ninguna app", () => {
    const files: string[] = [];
    const walk = (dir: string) => {
      for (const name of readdirSync(dir)) {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) walk(path);
        else if (/\.(ts|tsx)$/.test(name) && !path.includes("__tests__")) files.push(path);
      }
    };
    walk(join(__dirname, ".."));
    for (const file of files) {
      expect(readFileSync(file, "utf8"), file).not.toMatch(/from ["'](@\/|\.\.\/\.\.\/\.\.\/(frontend|frontend-hub))/);
    }
  });
});

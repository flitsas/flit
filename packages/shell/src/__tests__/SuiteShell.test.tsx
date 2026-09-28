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
    fireEvent.click(screen.getByRole("button", { name: "Menú de cuenta" }));
    expect(screen.getByRole("link", { name: "Cerrar sesión" })).toHaveAttribute("href", "/auth/logout");
  });

  it("el menú de productos lista los productos y marca el actual", () => {
    const apps = [
      { code: "plataforma", name: "Plataforma", icon: "layout-grid", url: "https://dev.flitsas.online", current: false },
      { code: "tramites", name: "Trámites", icon: "file-text", url: "https://dev.tramites.flitsas.online", current: true },
    ];
    render(<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} apps={apps}>x</SuiteShell>);

    fireEvent.click(screen.getByRole("button", { name: "Productos" }));
    const panel = screen.getByRole("region", { name: "Productos" });
    expect(within(panel).getByRole("link", { name: "Inicio" })).toHaveAttribute("href", "https://dev.flitsas.online");
    expect(within(panel).getByRole("link", { name: "Trámites" })).toHaveAttribute("aria-current", "page");
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

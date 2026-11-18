// Submenú desplegable dentro del panel del dock (Administración → Portal Persona Natural ▸ Reportes, Cobro, Caja, Diseño):
// botón con ▸/▾ y aria-expanded, abre y cierra con el teclado, y cada opción es un enlace directo a su sección.
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { Box } from "lucide-react";
import { buildDock, type NavCatalog } from "../nav";
import { Dock } from "../dock/Dock";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));
afterEach(cleanup);

const catalogo: NavCatalog = {
  sections: [{ id: "adm", label: "Administración", icon: Box, side: "right" }],
  items: [
    { key: "docs", label: "Documentos", href: "/docs", section: "adm", icon: Box, roles: ["ot_admin"] },
    {
      key: "portal",
      label: "Portal Persona Natural",
      href: "",
      section: "adm",
      icon: Box,
      desplegable: true,
      children: [
        { key: "p-rep", label: "Reportes", href: "/p?tab=reportes", section: "adm", icon: Box, roles: ["ot_admin"] },
        { key: "p-cobro", label: "Cobro", href: "/p?tab=cobro", section: "adm", icon: Box, roles: ["ot_admin"] },
        { key: "p-caja", label: "Caja", href: "/p?tab=caja", section: "adm", icon: Box, roles: ["ot_caja", "ot_admin"] },
        { key: "p-dis", label: "Diseño", href: "/p?tab=diseno", section: "adm", icon: Box, roles: ["ot_admin"] },
      ],
    },
  ],
};

function pintar(roles: string[]) {
  const groups = buildDock(catalogo, { permissions: [], isSuperAdmin: false, roles }, "/");
  render(<Dock groups={groups} homeHref="/" homeLabel="Inicio" homeIconSrc="/favicon.svg" homeActive={false} />);
}

describe("DockSubmenu", () => {
  it("el panel lista el submenú cerrado y se despliega con clic mostrando las 4 opciones como enlaces", () => {
    pintar(["ot_admin"]);
    fireEvent.click(screen.getByRole("button", { name: /Administración/ }));
    const submenu = screen.getByRole("button", { name: /Portal Persona Natural/ });
    expect(submenu).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("link", { name: "Reportes" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Documentos" })).toBeInTheDocument();

    fireEvent.click(submenu);
    expect(submenu).toHaveAttribute("aria-expanded", "true");
    const lista = document.getElementById(submenu.getAttribute("aria-controls") as string) as HTMLElement;
    expect(within(lista).getAllByRole("link").map((a) => [a.textContent, a.getAttribute("href")])).toEqual([
      ["Reportes", "/p?tab=reportes"],
      ["Cobro", "/p?tab=cobro"],
      ["Caja", "/p?tab=caja"],
      ["Diseño", "/p?tab=diseno"],
    ]);
  });

  it("es un botón nativo enfocable (Enter y Espacio lo activan) que alterna aria-expanded", () => {
    pintar(["ot_admin"]);
    fireEvent.click(screen.getByRole("button", { name: /Administración/ }));
    const submenu = screen.getByRole("button", { name: /Portal Persona Natural/ });
    expect(submenu.tagName).toBe("BUTTON");
    expect(submenu).toHaveAttribute("type", "button");
    submenu.focus();
    expect(submenu).toHaveFocus();
    fireEvent.click(submenu);
    expect(submenu).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(submenu);
    expect(submenu).toHaveAttribute("aria-expanded", "false");
  });

  it("la persona de caja solo ve la opción Caja", () => {
    pintar(["ot_caja"]);
    fireEvent.click(screen.getByRole("button", { name: /Administración|Portal Persona Natural/ }));
    const submenu = screen.getAllByRole("button", { name: /Portal Persona Natural/ }).at(-1) as HTMLElement;
    fireEvent.click(submenu);
    expect(screen.queryByRole("link", { name: "Documentos" })).not.toBeInTheDocument();
    const lista = document.getElementById(submenu.getAttribute("aria-controls") as string) as HTMLElement;
    expect(within(lista).getAllByRole("link").map((a) => a.textContent)).toEqual(["Caja"]);
  });
});

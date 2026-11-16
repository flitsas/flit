import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { Box } from "lucide-react";
import { SuiteShell } from "../SuiteShell";
import { useShellNav } from "../ShellNavContext";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));
afterEach(cleanup);

// HU #14480/#14481 — el buscador y el acordeón de Inicio usan los MISMOS grupos que el dock.
const nav = {
  sections: [{ id: "operacion", label: "Operación", icon: Box, side: "left" as const }],
  items: [
    { key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: Box },
    { key: "usuarios", label: "Usuarios", href: "/?m=usuarios", section: "operacion", icon: Box, permission: "usuarios.ver" },
  ],
};
const user = { email: "ana@empresa.co", permissions: [], isSuperAdmin: false };

function Espia() {
  const { groups, homeHref } = useShellNav();
  return <p data-testid="espia">{`${homeHref}|${groups.flatMap((g) => g.items.map((i) => i.key)).join(",")}`}</p>;
}

describe("SuiteShell: buscador Ctrl K", () => {
  it.each(["app", "page"] as const)("layout %s: botón en la barra y contexto con los grupos filtrados", (layout) => {
    render(
      <SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={user} layout={layout}>
        <Espia />
      </SuiteShell>,
    );
    expect(screen.getByTestId("espia")).toHaveTextContent("/|tramites");

    fireEvent.click(within(screen.getByRole("banner")).getByRole("button", { name: "Buscar (Ctrl K)" }));
    const lista = screen.getByRole("listbox", { name: "Resultados" });
    expect(within(lista).getByRole("option", { name: "Trámites" })).toHaveAttribute("href", "/tramites");
    expect(within(lista).queryByRole("option", { name: /Usuarios/ })).toBeNull();
  });

  it("pasa al buscador las acciones y la placa del producto", () => {
    render(
      <SuiteShell
        productCode="tramites"
        productName="Trámites"
        nav={nav}
        user={user}
        searchActions={[{ key: "x", label: "Mis trámites", href: "/tramites?atajo=mis_tramites", requires: "tramites" }]}
        plateSearch={{ href: (p) => `/tramites?placa=${p}`, hint: "Buscar en Trámites", requires: "tramites" }}
      >
        x
      </SuiteShell>,
    );
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "mis" } });
    expect(screen.getByRole("option", { name: /Mis trámites/ })).toHaveAttribute("href", "/tramites?atajo=mis_tramites");
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "XYZ987" } });
    expect(screen.getByRole("option", { name: /XYZ987/ })).toHaveAttribute("href", "/tramites?placa=XYZ987");
  });
});

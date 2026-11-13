import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { EstadoVacio } from "@flit/ui/EstadoVacio";

// Uso de ejemplo: <EstadoVacio icono={<IconoDxPendiente />} titulo="Aún no tienes diagnósticos"
//   mensaje="Diagnostica un vehículo o carga un archivo." acciones={[<Button key="a">Diagnosticar</Button>]} />

describe("EstadoVacio (HU #14562 · UI-1)", () => {
  it("muestra el título como encabezado, el mensaje y el ícono decorativo en disco", () => {
    const { container } = render(
      <EstadoVacio icono={<svg data-testid="ic" />} titulo="Aún no tienes diagnósticos" mensaje="Diagnostica un vehículo." />,
    );
    expect(screen.getByRole("heading", { level: 2, name: "Aún no tienes diagnósticos" })).toBeInTheDocument();
    expect(screen.getByText("Diagnostica un vehículo.")).toBeInTheDocument();
    const disco = container.querySelector("[data-parte='icono']") as HTMLElement;
    expect(disco.getAttribute("aria-hidden")).toBe("true");
    expect(disco).toContainElement(screen.getByTestId("ic"));
  });

  it("pinta como máximo 2 acciones", () => {
    render(
      <EstadoVacio
        titulo="Vacío"
        acciones={[<button key="1">Uno</button>, <button key="2">Dos</button>, <button key="3">Tres</button>]}
      />,
    );
    expect(screen.getAllByRole("button").map((b) => b.textContent)).toEqual(["Uno", "Dos"]);
  });

  it("sin acciones ni mensaje no deja contenedores vacíos", () => {
    const { container } = render(<EstadoVacio titulo="Vacío" />);
    expect(container.querySelector("[data-parte='acciones']")).toBeNull();
    expect(container.querySelector("p")).toBeNull();
  });

  it("anunciar=true lo vuelve role=status (vacío por filtros); por defecto no", () => {
    const { rerender } = render(<EstadoVacio titulo="Ningún diagnóstico coincide" />);
    expect(screen.queryByRole("status")).toBeNull();
    rerender(<EstadoVacio titulo="Ningún diagnóstico coincide" anunciar />);
    expect(screen.getByRole("status")).toHaveTextContent("Ningún diagnóstico coincide");
  });

  it("el nivel del encabezado se puede ajustar", () => {
    render(<EstadoVacio titulo="Elige una compañía" nivelTitulo={3} />);
    expect(screen.getByRole("heading", { level: 3, name: "Elige una compañía" })).toBeInTheDocument();
  });
});

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ListaDefinicion } from "@flit/ui/ListaDefinicion";

// Uso de ejemplo: <ListaDefinicion columnas={2} items={[{ termino: "Inicio", valor: "12/11/2026 10:05" },
//   { termino: "Intentos", valor: null, vacio: "El proveedor no lo informó" }]} />

describe("ListaDefinicion (HU #14562 · UI-1)", () => {
  it("pinta pares dt/dd en un dl", () => {
    const { container } = render(
      <ListaDefinicion items={[{ termino: "Inicio", valor: "10:05" }, { termino: "Fin", valor: "10:06" }]} />,
    );
    const dl = container.querySelector("dl") as HTMLElement;
    expect(dl.querySelectorAll("dt")).toHaveLength(2);
    expect(dl.querySelectorAll("dd")).toHaveLength(2);
    expect(screen.getByText("Inicio").tagName).toBe("DT");
    expect(screen.getByText("10:05").tagName).toBe("DD");
  });

  it("un valor ausente muestra el texto vacío del ítem o el de la lista, nunca un hueco", () => {
    render(
      <ListaDefinicion
        vacio="Sin dato"
        items={[
          { termino: "Intentos", valor: null, vacio: "El proveedor no lo informó" },
          { termino: "Duración", valor: "" },
          { termino: "Cero", valor: 0 },
        ]}
      />,
    );
    expect(screen.getByText("El proveedor no lo informó").tagName).toBe("DD");
    expect(screen.getByText("Sin dato").tagName).toBe("DD");
    expect(screen.getByText("0").tagName).toBe("DD");
  });

  it("acepta 1 o 2 columnas", () => {
    const { container, rerender } = render(<ListaDefinicion items={[{ termino: "a", valor: "b" }]} />);
    expect(container.querySelector("dl")).toHaveAttribute("data-columnas", "1");
    rerender(<ListaDefinicion columnas={2} items={[{ termino: "a", valor: "b" }]} />);
    expect(container.querySelector("dl")).toHaveAttribute("data-columnas", "2");
  });

  it("sin ítems muestra el estado vacío de la lista", () => {
    const { container } = render(<ListaDefinicion items={[]} vacioLista="Sin auditoría para esta consulta" />);
    expect(container.querySelector("dl")).toBeNull();
    expect(screen.getByText("Sin auditoría para esta consulta")).toBeInTheDocument();
  });
});

import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";

// Uso de ejemplo: <BarraSeleccion cantidad={2} textoSeleccion={(n) => `${n} vehículos seleccionados`}
//   acciones={<Button>Crear trámites (2)</Button>} onQuitar={limpiar} ayuda="Solo Viables y sin trámite" />

describe("BarraSeleccion (HU #14562 · UI-2)", () => {
  it("sin selección no se pinta", () => {
    const { container } = render(<BarraSeleccion cantidad={0} acciones={<button>Crear</button>} />);
    expect(container.innerHTML).toBe("");
  });

  it("con selección es una región con nombre y conteo en aria-live polite", () => {
    render(<BarraSeleccion cantidad={2} acciones={<button>Crear trámites (2)</button>} />);
    const region = screen.getByRole("region", { name: "Selección" });
    const vivo = region.querySelector("[aria-live='polite']") as HTMLElement;
    expect(vivo).toHaveTextContent("2 seleccionados");
    expect(screen.getByRole("button", { name: "Crear trámites (2)" })).toBeInTheDocument();
  });

  it("singular con 1 y texto propio con textoSeleccion", () => {
    const { rerender } = render(<BarraSeleccion cantidad={1} acciones={null} />);
    expect(screen.getByText("1 seleccionado")).toBeInTheDocument();
    rerender(<BarraSeleccion cantidad={3} acciones={null} textoSeleccion={(n) => `${n} vehículos seleccionados`} />);
    expect(screen.getByText("3 vehículos seleccionados")).toBeInTheDocument();
  });

  it("«Quitar selección» llama onQuitar; sin onQuitar no aparece", () => {
    const onQuitar = vi.fn();
    const { rerender } = render(<BarraSeleccion cantidad={2} acciones={null} onQuitar={onQuitar} />);
    fireEvent.click(screen.getByRole("button", { name: "Quitar selección" }));
    expect(onQuitar).toHaveBeenCalledTimes(1);
    rerender(<BarraSeleccion cantidad={2} acciones={null} />);
    expect(screen.queryByRole("button", { name: "Quitar selección" })).toBeNull();
  });

  it("muestra la ayuda de la regla de elegibilidad", () => {
    render(<BarraSeleccion cantidad={2} acciones={null} ayuda="Solo Viables y sin trámite" />);
    expect(screen.getByText("Solo Viables y sin trámite")).toBeInTheDocument();
  });
});

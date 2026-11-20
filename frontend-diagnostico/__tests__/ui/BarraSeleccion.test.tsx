import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { BarraSeleccion } from "@flit/ui/BarraSeleccion";

// Uso de ejemplo: <BarraSeleccion cantidad={2} textoSeleccion={(n) => `${n} vehículos seleccionados`}
//   acciones={<Button>Crear trámites (2)</Button>} onQuitar={limpiar} ayuda="Solo Viables y sin trámite" />

describe("BarraSeleccion (HU #14562 · UI-2)", () => {
  it("sin selección no se pinta", () => {
    const { container } = render(<BarraSeleccion cantidad={0} acciones={<button>Crear</button>} />);
    expect(container.innerHTML).toBe("");
  });

  it("con selección es una región con nombre y conteo en aria-live polite", async () => {
    render(<BarraSeleccion cantidad={2} acciones={<button>Crear trámites (2)</button>} />);
    const region = screen.getByRole("region", { name: "Selección" });
    const vivo = region.querySelector("[aria-live='polite']") as HTMLElement;
    await waitFor(() => expect(vivo).toHaveTextContent("2 seleccionados"));
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

// HU #14562 · M11 (auditoría): la barra aparece al pasar de 0 a 1; su región viva nace vacía y el conteo llega después,
// para que el primer «1 seleccionado» se anuncie. El conteo visible queda fuera del árbol accesible (sin doble lectura).
describe("BarraSeleccion: región viva diferida (HU #14562 · M11)", () => {
  it("M11: al aparecer, la región viva está vacía, el conteo visible ya se ve y luego se anuncia", async () => {
    render(<BarraSeleccion cantidad={1} acciones={null} />);
    const region = screen.getByRole("region", { name: "Selección" });
    const vivo = region.querySelector("[aria-live='polite']") as HTMLElement;
    expect(vivo).toBeEmptyDOMElement();
    const visible = screen.getByText("1 seleccionado");
    expect(visible).toHaveAttribute("aria-hidden", "true");
    await waitFor(() => expect(vivo).toHaveTextContent("Selección: 1 seleccionado"));
  });
});

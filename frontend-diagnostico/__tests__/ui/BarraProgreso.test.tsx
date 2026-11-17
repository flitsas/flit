import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { BarraProgreso } from "@flit/ui/BarraProgreso";

// Uso de ejemplo: <BarraProgreso etiqueta="Avance del lote" valor={18} maximo={24} textoValor="18 de 24 vehículos terminados" />

describe("BarraProgreso (HU #14562 · UI-1)", () => {
  it("es un progressbar con nombre, valores y texto accesible", () => {
    render(<BarraProgreso etiqueta="Avance del lote" valor={18} maximo={24} textoValor="18 de 24 vehículos terminados" />);
    const barra = screen.getByRole("progressbar", { name: "Avance del lote" });
    expect(barra).toHaveAttribute("aria-valuenow", "18");
    expect(barra).toHaveAttribute("aria-valuemin", "0");
    expect(barra).toHaveAttribute("aria-valuemax", "24");
    expect(barra).toHaveAttribute("aria-valuetext", "18 de 24 vehículos terminados");
  });

  it("pinta el relleno proporcional", () => {
    const { container } = render(<BarraProgreso etiqueta="Avance" valor={1} maximo={4} />);
    expect((container.querySelector("[data-parte='relleno']") as HTMLElement).style.width).toBe("25%");
  });

  it("acota el valor entre 0 y el máximo", () => {
    const { rerender } = render(<BarraProgreso etiqueta="Avance" valor={40} maximo={24} />);
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "24");
    rerender(<BarraProgreso etiqueta="Avance" valor={-3} maximo={24} />);
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
  });

  it("con máximo 0 (lote sin vehículos) no produce NaN", () => {
    const { container } = render(<BarraProgreso etiqueta="Avance" valor={0} maximo={0} />);
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
    expect((container.querySelector("[data-parte='relleno']") as HTMLElement).style.width).toBe("0%");
  });

  it("el relleno anima solo con movimiento permitido", () => {
    const { container } = render(<BarraProgreso etiqueta="Avance" valor={1} maximo={2} />);
    const cls = (container.querySelector("[data-parte='relleno']") as HTMLElement).className;
    expect(cls).toContain("motion-reduce:transition-none");
    expect(cls).not.toMatch(/(^|\s)animate-/);
  });
});

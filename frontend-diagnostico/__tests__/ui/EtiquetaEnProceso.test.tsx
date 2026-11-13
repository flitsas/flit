import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";

// Uso de ejemplo: <EtiquetaEnProceso /> en la cabecera del tablero mientras el lote sigue consultando.

describe("EtiquetaEnProceso (HU #14562 · UI-1, AC5)", () => {
  it("comunica el estado con texto visible, no solo con color", () => {
    render(<EtiquetaEnProceso />);
    const etiqueta = screen.getByRole("status", { name: "Estado: En proceso" });
    expect(etiqueta).toHaveTextContent("En proceso");
    expect(etiqueta).toHaveAttribute("data-tono", "info");
    expect(etiqueta).toHaveAttribute("data-estado", "en-proceso");
  });

  it("el punto pulsa solo con movimiento permitido (prefers-reduced-motion lo deja quieto)", () => {
    const { container } = render(<EtiquetaEnProceso />);
    const punto = container.querySelector("[data-marca='punto']") as HTMLElement;
    expect(punto.getAttribute("aria-hidden")).toBe("true");
    expect(punto.className).toContain("motion-safe:animate-pulse");
    expect(punto.className.split(/\s+/).filter((c) => c.startsWith("animate-"))).toEqual([]);
  });

  it("acepta otro texto", () => {
    render(<EtiquetaEnProceso etiqueta="Consultando" />);
    expect(screen.getByRole("status", { name: "Estado: Consultando" })).toHaveTextContent("Consultando");
  });
});

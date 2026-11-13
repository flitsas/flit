import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";

// Uso de ejemplo: <EtiquetaEnProceso /> en la cabecera del tablero mientras el lote sigue consultando.

describe("EtiquetaEnProceso (HU #14562 · UI-1, AC5)", () => {
  it("comunica el estado con texto visible, no solo con color", async () => {
    render(<EtiquetaEnProceso />);
    const etiqueta = await screen.findByRole("status", { name: "Estado: En proceso" });
    expect(etiqueta).toHaveTextContent("En proceso");
    expect(etiqueta).toHaveAttribute("data-tono", "info");
    expect(etiqueta).toHaveAttribute("data-estado", "en-proceso");
  });

  it("el punto pulsa solo con movimiento permitido (prefers-reduced-motion lo deja quieto)", async () => {
    const { container } = render(<EtiquetaEnProceso />);
    await screen.findByRole("status");
    const punto = container.querySelector("[data-marca='punto']") as HTMLElement;
    expect(punto.getAttribute("aria-hidden")).toBe("true");
    expect(punto.className).toContain("motion-safe:animate-pulse");
    expect(punto.className.split(/\s+/).filter((c) => c.startsWith("animate-"))).toEqual([]);
  });

  it("acepta otro texto", async () => {
    render(<EtiquetaEnProceso etiqueta="Consultando" />);
    expect(await screen.findByRole("status", { name: "Estado: Consultando" })).toHaveTextContent("Consultando");
  });
});

// HU #14562 · M11 (auditoría, mismo patrón que la notificación): la región viva nace vacía y el texto llega después.
describe("EtiquetaEnProceso: región viva diferida (HU #14562 · M11)", () => {
  it("M11: se monta como aria-live vacía y luego recibe el estado", async () => {
    const { container } = render(<EtiquetaEnProceso />);
    const region = container.firstElementChild as HTMLElement;
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toBeEmptyDOMElement();
    expect(await screen.findByRole("status", { name: "Estado: En proceso" })).toBe(region);
  });
});

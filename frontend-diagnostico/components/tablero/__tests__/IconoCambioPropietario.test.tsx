import { describe, expect, it } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { IconoCambioPropietario, TEXTO_CAMBIO_PROPIETARIO } from "@/components/tablero/IconoCambioPropietario";

// Uso de ejemplo: <IconoCambioPropietario cambio={fila.cambio_propietario} /> junto a la placa → con `true`, botón 24 × 24
// «Cambio de propietario» con el tooltip literal; con `false` o `null`, nada.
// HU #14585 (spec-ui §6.5, A6.7; contrato 1.6.0 §1.2). SP-19: el literal solo se monta al abrir el tooltip.

describe("IconoCambioPropietario — HU #14585 A6.7", () => {
  it("con true pinta un botón de 24 × 24 con nombre «Cambio de propietario»", () => {
    render(<IconoCambioPropietario cambio />);
    const boton = screen.getByRole("button", { name: "Cambio de propietario" });
    expect(boton).toHaveAttribute("type", "button");
    expect(boton.className).toMatch(/\bh-6\b/);
    expect(boton.className).toMatch(/\bw-6\b/);
    expect(boton.className).toContain("rounded-full");
    expect(boton.className).toContain("focus-visible:outline-2");
  });

  it.each([false, null, undefined])("con %s no pinta nada", (cambio) => {
    const { container } = render(<IconoCambioPropietario cambio={cambio} />);
    expect(container.innerHTML).toBe("");
  });

  it("el foco abre el tooltip con el texto exacto del contrato y lo enlaza con aria-describedby", () => {
    render(<IconoCambioPropietario cambio />);
    const boton = screen.getByRole("button", { name: "Cambio de propietario" });
    expect(screen.queryByRole("tooltip")).toBeNull();
    act(() => boton.focus());
    const tip = screen.getByRole("tooltip");
    expect(tip).toHaveTextContent(TEXTO_CAMBIO_PROPIETARIO);
    expect(boton.getAttribute("aria-describedby") ?? "").toContain(tip.id);
    expect(TEXTO_CAMBIO_PROPIETARIO).toBe(
      "Hubo un cambio de propietario: el documento declarado no coincide con el propietario actual reportado por el RUNT.",
    );
    fireEvent.keyDown(boton, { key: "Escape" });
    expect(screen.queryByRole("tooltip")).toBeNull();
    expect(boton).toHaveFocus();
  });

  it("SP-19: cerrado, el literal no está en el DOM (ni oculto)", () => {
    const { container } = render(<IconoCambioPropietario cambio />);
    expect(container.ownerDocument.body.innerHTML).not.toContain("no coincide con el propietario actual");
  });

  it("< 900 px: texto visible «Cambio de propietario» aria-hidden (el botón ya lo nombra)", () => {
    const { container } = render(<IconoCambioPropietario cambio />);
    const texto = container.querySelector("[data-cambio-propietario-movil]")!;
    expect(texto).toHaveTextContent("Cambio de propietario");
    expect(texto).toHaveAttribute("aria-hidden", "true");
    expect(texto.className).toContain("hidden");
    expect(texto.className).toContain("max-[900px]:inline");
  });

  it("el ícono es decorativo y usa el token de advertencia, sin hex", () => {
    const { container } = render(<IconoCambioPropietario cambio />);
    expect(container.innerHTML).toContain("--di-warning");
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,6}\b/);
  });
});

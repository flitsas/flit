import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Button, buttonClass } from "@flit/ui/Button";

// Uso de ejemplo: <Button cargando={enviando}>Iniciar diagnóstico</Button>

describe("Button.cargando (HU #14562 · UI-1, AC7)", () => {
  it("cargando=true: deshabilitado, aria-busy y girador visible junto al texto", () => {
    render(<Button cargando>Iniciar diagnóstico</Button>);
    const boton = screen.getByRole("button", { name: "Iniciar diagnóstico" });
    expect(boton).toBeDisabled();
    expect(boton).toHaveAttribute("aria-busy", "true");
    const girador = boton.querySelector("[data-parte='girador']") as SVGElement;
    expect(girador).not.toBeNull();
    expect(girador.getAttribute("aria-hidden")).toBe("true");
    expect(girador.getAttribute("class")).toContain("motion-safe:animate-spin");
  });

  it("cargando no dispara onClick", () => {
    const onClick = vi.fn();
    render(
      <Button cargando onClick={onClick}>
        Guardar
      </Button>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Guardar" }));
    expect(onClick).not.toHaveBeenCalled();
  });

  it("AC6: sin la prop cargando se renderiza igual que antes (retrocompatible)", () => {
    const { container } = render(<Button>Guardar</Button>);
    expect(container.innerHTML).toBe(`<button type="button" class="${buttonClass()}">Guardar</button>`);
  });

  it("AC6: cargando=false tampoco cambia el marcado, y respeta disabled y aria-busy propios", () => {
    const { container } = render(
      <Button variant="alerta" size="sm" cargando={false} disabled aria-busy="true">
        Cancelar
      </Button>,
    );
    expect(container.innerHTML).toBe(
      `<button type="button" class="${buttonClass({ variant: "alerta", size: "sm" })}" disabled="" aria-busy="true">Cancelar</button>`,
    );
  });
});

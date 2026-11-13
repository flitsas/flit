import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { Button, buttonClass } from "@flit/ui/Button";

// Uso de ejemplo: <Button cargando={enviando}>Iniciar diagnóstico</Button>

describe("Button.cargando (HU #14562 · UI-1, AC7)", () => {
  it("M9: cargando=true: aria-disabled (no disabled, para no soltar el foco), aria-busy y girador junto al texto", () => {
    render(<Button cargando>Iniciar diagnóstico</Button>);
    const boton = screen.getByRole("button", { name: "Iniciar diagnóstico" });
    expect(boton).not.toBeDisabled();
    expect(boton).toHaveAttribute("aria-disabled", "true");
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

// HU #14562 · M9 (auditoría): al pasar a cargando el botón que tiene el foco lo conserva (2.4.3) y sigue inoperable.
describe("Button.cargando conserva el foco (HU #14562 · M9)", () => {
  it("M9: el botón enfocado sigue enfocado al pasar a cargando", () => {
    const { rerender } = render(<Button>Continuar</Button>);
    const boton = screen.getByRole("button", { name: "Continuar" });
    boton.focus();
    rerender(<Button cargando>Continuar</Button>);
    expect(screen.getByRole("button", { name: "Continuar" })).toBe(boton);
    expect(boton).toHaveFocus();
  });

  it("M9: cargando dentro de un formulario no lo envía (type=submit)", () => {
    const onSubmit = vi.fn((e: React.FormEvent) => e.preventDefault());
    render(
      <form onSubmit={onSubmit}>
        <Button type="submit" cargando>
          Validar archivo
        </Button>
      </form>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("M9: cargando con estilo de espera del kit (gris), igual que antes con disabled", () => {
    render(<Button cargando>Guardar</Button>);
    expect(screen.getByRole("button", { name: "Guardar" }).className).toContain("aria-disabled:bg-[color:var(--kit-gris)]");
  });

  it("M9: un disabled del llamador (p. ej. «ocupado») no se aplica mientras carga, para no soltar el foco", () => {
    const onClick = vi.fn();
    render(
      <Button cargando disabled onClick={onClick}>
        Continuar
      </Button>,
    );
    const boton = screen.getByRole("button", { name: "Continuar" });
    expect(boton).not.toBeDisabled();
    expect(boton).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(boton);
    expect(onClick).not.toHaveBeenCalled();
  });
});

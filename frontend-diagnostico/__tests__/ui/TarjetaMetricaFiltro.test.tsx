import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { TarjetaMetrica } from "@flit/ui/TarjetaMetrica";

// Uso de ejemplo: <TarjetaMetrica etiqueta="Con error" cifra={3} activa={filtro === "error"}
//   onAlternar={(on) => setFiltro(on ? "error" : null)} />

describe("TarjetaMetrica como filtro (HU #14562 · UI-2, AC7)", () => {
  it("con onAlternar es un botón nativo con aria-pressed (Enter y Espacio por el navegador)", () => {
    render(<TarjetaMetrica etiqueta="Con error" cifra={3} onAlternar={vi.fn()} />);
    const boton = screen.getByRole("button", { name: /Con error/ });
    expect(boton.tagName).toBe("BUTTON");
    expect(boton).toHaveAttribute("type", "button");
    expect(boton).toHaveAttribute("aria-pressed", "false");
  });

  it("alterna entre true y false", () => {
    const onAlternar = vi.fn();
    const { rerender } = render(<TarjetaMetrica etiqueta="Con error" cifra={3} onAlternar={onAlternar} />);
    fireEvent.click(screen.getByRole("button"));
    expect(onAlternar).toHaveBeenLastCalledWith(true);
    rerender(<TarjetaMetrica etiqueta="Con error" cifra={3} activa onAlternar={onAlternar} />);
    expect(screen.getByRole("button")).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button")).toHaveAttribute("data-activa", "true");
    fireEvent.click(screen.getByRole("button"));
    expect(onAlternar).toHaveBeenLastCalledWith(false);
  });

  it("AC6: sin activa/onAlternar es una tarjeta quieta, como antes", () => {
    const { container } = render(<TarjetaMetrica etiqueta="Vehículos" cifra={24} />);
    expect(container.firstElementChild?.tagName).toBe("DIV");
    expect(container.querySelector("[aria-pressed]")).toBeNull();
  });

  it("AC6: con solo onClick sigue siendo un botón sin aria-pressed", () => {
    const onClick = vi.fn();
    render(<TarjetaMetrica etiqueta="Vehículos" cifra={24} onClick={onClick} />);
    const boton = screen.getByRole("button");
    expect(boton).not.toHaveAttribute("aria-pressed");
    fireEvent.click(boton);
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

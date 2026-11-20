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

// HU #14562 · M4 (auditoría): activa ≠ hover. La activa lleva un indicador no cromático (marca de check) y un borde de
// 2 px en --kit-azul-texto (3,33:1 sobre la tarjeta); el hover del modo filtro ya no pinta ese borde azul.
describe("TarjetaMetrica filtro: activa distinguible del hover (HU #14562 · M4)", () => {
  it("M4: activa muestra la marca de filtro activo (no solo color) y el borde de 2 px", () => {
    const { container } = render(<TarjetaMetrica etiqueta="Con error" cifra={3} activa onAlternar={vi.fn()} />);
    const boton = screen.getByRole("button");
    const marca = container.querySelector("[data-parte='marca-activa']") as HTMLElement;
    expect(marca).not.toBeNull();
    expect(marca.getAttribute("aria-hidden")).toBe("true");
    expect(marca.querySelector("svg")).not.toBeNull();
    expect(boton.className).toContain("shadow-[inset_0_0_0_1px_var(--kit-azul-texto)]");
    expect(boton.className).toContain("border-[color:var(--kit-azul-texto)]");
  });

  it("M4: inactiva no lleva la marca y su hover no usa el borde azul de la activa", () => {
    const { container } = render(<TarjetaMetrica etiqueta="Con error" cifra={3} onAlternar={vi.fn()} />);
    expect(container.querySelector("[data-parte='marca-activa']")).toBeNull();
    const clases = screen.getByRole("button").className;
    expect(clases).not.toContain("hover:border-[color:var(--kit-azul-texto)]");
    expect(clases).toContain("hover:bg-[color:var(--kit-hover)]");
  });

  it("M4 retrocompatible: la tarjeta de enlace o de clic conserva el hover de borde azul de siempre", () => {
    render(<TarjetaMetrica etiqueta="Vehículos" cifra={24} onClick={vi.fn()} />);
    expect(screen.getByRole("button").className).toContain("hover:border-[color:var(--kit-azul-texto)]");
  });
});

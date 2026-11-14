import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { TarjetaPlegable } from "@flit/ui/TarjetaPlegable";

// Uso de ejemplo: <TarjetaPlegable titulo="Respuesta del proveedor"><pre>{json}</pre></TarjetaPlegable>

describe("TarjetaPlegable (HU #14562 · UI-1)", () => {
  it("arranca plegada: botón con aria-expanded=false y contenido oculto", () => {
    render(
      <TarjetaPlegable titulo="Respuesta del proveedor">
        <p>contenido</p>
      </TarjetaPlegable>,
    );
    const boton = screen.getByRole("button", { name: "Respuesta del proveedor" });
    expect(boton).toHaveAttribute("aria-expanded", "false");
    const region = document.getElementById(boton.getAttribute("aria-controls") as string) as HTMLElement;
    expect(region).not.toBeNull();
    expect(region.hidden).toBe(true);
  });

  it("al pulsar se despliega y vuelve a plegarse", () => {
    render(
      <TarjetaPlegable titulo="Respuesta del proveedor">
        <p>contenido</p>
      </TarjetaPlegable>,
    );
    const boton = screen.getByRole("button", { name: "Respuesta del proveedor" });
    fireEvent.click(boton);
    expect(boton).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("contenido")).toBeVisible();
    fireEvent.click(boton);
    expect(boton).toHaveAttribute("aria-expanded", "false");
  });

  it("abiertaInicial la monta desplegada", () => {
    render(
      <TarjetaPlegable titulo="Estimado" abiertaInicial>
        <p>cifras</p>
      </TarjetaPlegable>,
    );
    expect(screen.getByRole("button", { name: "Estimado" })).toHaveAttribute("aria-expanded", "true");
  });

  it("modo controlado: respeta abierta y avisa con onAlternar", () => {
    const onAlternar = vi.fn();
    render(
      <TarjetaPlegable titulo="Estimado" abierta={false} onAlternar={onAlternar}>
        <p>cifras</p>
      </TarjetaPlegable>,
    );
    const boton = screen.getByRole("button", { name: "Estimado" });
    fireEvent.click(boton);
    expect(onAlternar).toHaveBeenCalledWith(true);
    expect(boton).toHaveAttribute("aria-expanded", "false");
  });

  it("el título va dentro de un encabezado (h3 por defecto)", () => {
    render(
      <TarjetaPlegable titulo="Estimado">
        <p>cifras</p>
      </TarjetaPlegable>,
    );
    expect(screen.getByRole("heading", { level: 3, name: "Estimado" })).toBeInTheDocument();
  });
});

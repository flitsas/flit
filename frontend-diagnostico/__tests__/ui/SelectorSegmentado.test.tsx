import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { SelectorSegmentado } from "@flit/ui/SelectorSegmentado";

// Uso de ejemplo: <SelectorSegmentado etiqueta="Tipo de diagnóstico" valor={modo} onCambio={setModo}
//   opciones={[{ valor: "express", label: "Express" }, { valor: "masivo", label: "Masivo" }]} />

const opciones = [
  { valor: "todos", label: "Todos" },
  { valor: "express", label: "Express" },
  { valor: "masivo", label: "Masivo" },
] as const;

describe("SelectorSegmentado (HU #14562 · UI-1)", () => {
  it("es un radiogroup con nombre, una opción marcada y solo ella en el orden de tabulación", () => {
    render(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="express" onCambio={vi.fn()} />);
    expect(screen.getByRole("radiogroup", { name: "Tipo" })).toBeInTheDocument();
    const express = screen.getByRole("radio", { name: "Express" });
    expect(express).toHaveAttribute("aria-checked", "true");
    expect(express).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("radio", { name: "Todos" })).toHaveAttribute("aria-checked", "false");
    expect(screen.getByRole("radio", { name: "Todos" })).toHaveAttribute("tabindex", "-1");
  });

  it("clic en una opción llama onCambio con su valor", () => {
    const onCambio = vi.fn();
    render(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="todos" onCambio={onCambio} />);
    fireEvent.click(screen.getByRole("radio", { name: "Masivo" }));
    expect(onCambio).toHaveBeenCalledWith("masivo");
  });

  it("flechas derecha e izquierda mueven la selección y el foco, con vuelta al principio", () => {
    const onCambio = vi.fn();
    const { rerender } = render(
      <SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="masivo" onCambio={onCambio} />,
    );
    screen.getByRole("radio", { name: "Masivo" }).focus();
    fireEvent.keyDown(screen.getByRole("radio", { name: "Masivo" }), { key: "ArrowRight" });
    expect(onCambio).toHaveBeenLastCalledWith("todos");
    expect(document.activeElement).toBe(screen.getByRole("radio", { name: "Todos" }));
    rerender(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="todos" onCambio={onCambio} />);
    fireEvent.keyDown(screen.getByRole("radio", { name: "Todos" }), { key: "ArrowLeft" });
    expect(onCambio).toHaveBeenLastCalledWith("masivo");
  });

  it("deshabilitado no llama onCambio", () => {
    const onCambio = vi.fn();
    render(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="todos" onCambio={onCambio} deshabilitado />);
    fireEvent.click(screen.getByRole("radio", { name: "Express" }));
    expect(onCambio).not.toHaveBeenCalled();
  });
});

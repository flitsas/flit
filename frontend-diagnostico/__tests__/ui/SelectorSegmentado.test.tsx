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

// HU #14562 · M10 (auditoría): describedBy, etiquetadoPor y el modo bloqueado, que sigue enfocable (aria-disabled y la
// opción marcada en tabIndex=0) para que teclado y lector lleguen a él y oigan por qué no cambia.
describe("SelectorSegmentado: describedBy, etiquetadoPor y bloqueado (HU #14562 · M10)", () => {
  it("M10: describedBy asocia la ayuda al radiogroup", () => {
    render(
      <>
        <SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="todos" onCambio={vi.fn()} describedBy="ayuda" />
        <p id="ayuda">Express: un vehículo.</p>
      </>,
    );
    expect(screen.getByRole("radiogroup", { name: "Tipo" })).toHaveAccessibleDescription("Express: un vehículo.");
  });

  it("M10: etiquetadoPor toma el nombre del rótulo visible y no duplica aria-label", () => {
    render(
      <>
        <span id="rotulo">Quién ve los diagnósticos</span>
        <SelectorSegmentado etiquetadoPor="rotulo" opciones={[...opciones]} valor="todos" onCambio={vi.fn()} />
      </>,
    );
    const grupo = screen.getByRole("radiogroup", { name: "Quién ve los diagnósticos" });
    expect(grupo).toHaveAttribute("aria-labelledby", "rotulo");
    expect(grupo).not.toHaveAttribute("aria-label");
  });

  it("M10: bloqueado sigue enfocable: aria-disabled en el grupo y las opciones, la marcada con tabIndex=0", () => {
    render(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="express" onCambio={vi.fn()} bloqueado />);
    expect(screen.getByRole("radiogroup", { name: "Tipo" })).toHaveAttribute("aria-disabled", "true");
    for (const radio of screen.getAllByRole("radio")) {
      expect(radio).not.toBeDisabled();
      expect(radio).toHaveAttribute("aria-disabled", "true");
    }
    const marcada = screen.getByRole("radio", { name: "Express" });
    expect(marcada).toHaveAttribute("tabindex", "0");
    marcada.focus();
    expect(marcada).toHaveFocus();
  });

  it("M10: bloqueado ignora el clic y las flechas, y no mueve el foco", () => {
    const onCambio = vi.fn();
    render(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="express" onCambio={onCambio} bloqueado />);
    const marcada = screen.getByRole("radio", { name: "Express" });
    marcada.focus();
    fireEvent.keyDown(marcada, { key: "ArrowRight" });
    fireEvent.keyDown(marcada, { key: "End" });
    fireEvent.click(screen.getByRole("radio", { name: "Masivo" }));
    expect(onCambio).not.toHaveBeenCalled();
    expect(marcada).toHaveFocus();
  });

  it("M10 retrocompatible: deshabilitado sigue siendo inerte (disabled nativo)", () => {
    render(<SelectorSegmentado etiqueta="Tipo" opciones={[...opciones]} valor="express" onCambio={vi.fn()} deshabilitado />);
    for (const radio of screen.getAllByRole("radio")) expect(radio).toBeDisabled();
  });
});

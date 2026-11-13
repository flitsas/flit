import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";

// Uso de ejemplo: <TablaEditable etiqueta="Fuentes" filas={filas} clave={(f) => f.id}
//   columnas={[{ campo: "nombre", titulo: "Nombre", editable: true }]} onGuardar={guardarFila} />

interface Fila { id: string; nombre: string; limite: string }
const filas: Fila[] = [
  { id: "A", nombre: "Alfa", limite: "10" },
  { id: "B", nombre: "Beta", limite: "20" },
];
const columnas: ColumnaEditable<Fila>[] = [
  { campo: "id", titulo: "Código" },
  { campo: "nombre", titulo: "Nombre", editable: true },
  { campo: "limite", titulo: "Límite", editable: true },
];

function fila(id: string) {
  return screen.getByRole("row", { name: id });
}

describe("TablaEditable (HU #14562 · AC9)", () => {
  it("AC9: editar una fila la pasa a Editado con Descartar y Guardar, sin tocar las demás", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    expect(screen.getByRole("table", { name: "Fuentes" })).toBeInTheDocument();
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 2" } });
    const a = fila("A");
    expect(within(a).getByText("Editado")).toBeInTheDocument();
    expect(within(a).getByRole("button", { name: "Guardar A" })).toBeInTheDocument();
    expect(within(a).getByRole("button", { name: "Descartar A" })).toBeInTheDocument();
    const b = fila("B");
    expect(within(b).getByText("Sin cambios")).toBeInTheDocument();
    expect(within(b).queryByRole("button", { name: "Guardar B" })).toBeNull();
  });

  it("AC9: Descartar restaura los valores y llama onDescartar con la fila original", () => {
    const onDescartar = vi.fn();
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} onDescartar={onDescartar} />);
    const input = screen.getByRole("textbox", { name: "Nombre de A" });
    fireEvent.change(input, { target: { value: "X" } });
    fireEvent.click(screen.getByRole("button", { name: "Descartar A" }));
    expect(input).toHaveValue("Alfa");
    expect(onDescartar).toHaveBeenCalledWith(filas[0]);
    expect(within(fila("A")).getByText("Sin cambios")).toBeInTheDocument();
  });

  it("AC9: guardar con promesa resuelta vuelve a Sin cambios y envía los valores digitados", async () => {
    let resolver!: () => void;
    const onGuardar = vi.fn(() => new Promise<void>((r) => { resolver = r; }));
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Límite de B" }), { target: { value: "25" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar B" }));
    expect(onGuardar).toHaveBeenCalledWith({ id: "B", nombre: "Beta", limite: "25" });
    expect(within(fila("B")).getByText("Guardando")).toBeInTheDocument();
    expect(within(fila("B")).getByRole("button", { name: "Guardar B" })).toHaveAttribute("aria-busy", "true");
    await act(async () => { resolver(); });
    expect(within(fila("B")).getByText("Sin cambios")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Límite de B" })).toHaveValue("25");
  });

  it("AC9 negativo: promesa rechazada deja Error de guardado con Reintentar, conserva lo digitado y mueve el foco", async () => {
    const onGuardar = vi.fn().mockRejectedValueOnce(new Error("500")).mockResolvedValueOnce(undefined);
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 9" } });
    await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Guardar A" })); });
    const a = fila("A");
    expect(within(a).getByText("Error de guardado")).toBeInTheDocument();
    const reintentar = within(a).getByRole("button", { name: "Reintentar A" });
    expect(document.activeElement).toBe(reintentar);
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Alfa 9");
    expect(within(fila("B")).getByText("Sin cambios")).toBeInTheDocument();
    await act(async () => { fireEvent.click(reintentar); });
    expect(onGuardar).toHaveBeenLastCalledWith({ id: "A", nombre: "Alfa 9", limite: "10" });
    expect(within(fila("A")).getByText("Sin cambios")).toBeInTheDocument();
  });

  it("sin filas muestra el texto de vacío", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={[]} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} vacio="No hay fuentes" />);
    expect(screen.getByText("No hay fuentes")).toBeInTheDocument();
  });
});

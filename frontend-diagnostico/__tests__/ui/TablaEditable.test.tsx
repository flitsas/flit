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

// HU #14562 · M3 (auditoría): el foco no cae en body al guardar o descartar y el cambio de estado se anuncia en una
// región viva propia de la tabla, que existe vacía antes del primer anuncio.
describe("TablaEditable: foco y anuncio (HU #14562 · M3)", () => {
  const region = () => screen.getByRole("status", { name: "Estado de Fuentes" });

  it("M3: la región viva de la tabla nace vacía y anuncia cada cambio de estado con la fila", async () => {
    let resolver!: () => void;
    const onGuardar = vi.fn(() => new Promise<void>((r) => { resolver = r; }));
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    expect(region()).toHaveTextContent(/^$/);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 2" } });
    expect(region()).toHaveTextContent("A: Editado");
    fireEvent.click(screen.getByRole("button", { name: "Guardar A" }));
    expect(region()).toHaveTextContent("A: Guardando");
    await act(async () => { resolver(); });
    expect(region()).toHaveTextContent("A: Guardado");
  });

  it("M3: al guardar con éxito el foco pasa a la celda de estado de la fila, no a body", async () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 2" } });
    const guardar = screen.getByRole("button", { name: "Guardar A" });
    guardar.focus();
    await act(async () => { fireEvent.click(guardar); });
    const estado = within(fila("A")).getByText("Sin cambios");
    expect(estado).toHaveFocus();
    expect(estado).toHaveAttribute("tabindex", "-1");
  });

  it("M3: al descartar el foco pasa a la celda de estado de la fila", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "X" } });
    const descartar = screen.getByRole("button", { name: "Descartar A" });
    descartar.focus();
    fireEvent.click(descartar);
    expect(within(fila("A")).getByText("Sin cambios")).toHaveFocus();
    expect(region()).toHaveTextContent("A: Sin cambios");
  });

  it("M3: si el usuario ya está en otra fila cuando termina el guardado, no se le roba el foco", async () => {
    let resolver!: () => void;
    const onGuardar = vi.fn(() => new Promise<void>((r) => { resolver = r; }));
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 2" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar A" }));
    const otro = screen.getByRole("textbox", { name: "Nombre de B" });
    otro.focus();
    await act(async () => { resolver(); });
    expect(otro).toHaveFocus();
  });

  it("M3: mientras guarda, Guardar queda aria-disabled (no disabled), conserva el foco e ignora otro clic", async () => {
    let resolver!: () => void;
    const onGuardar = vi.fn(() => new Promise<void>((r) => { resolver = r; }));
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 2" } });
    const guardar = screen.getByRole("button", { name: "Guardar A" });
    guardar.focus();
    fireEvent.click(guardar);
    expect(guardar).not.toBeDisabled();
    expect(guardar).toHaveAttribute("aria-disabled", "true");
    expect(guardar).toHaveFocus();
    fireEvent.click(guardar);
    fireEvent.click(screen.getByRole("button", { name: "Descartar A" }));
    expect(onGuardar).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Alfa 2");
    await act(async () => { resolver(); });
  });

  it("M3: errorFila pinta el motivo en la fila y lo asocia a Reintentar", async () => {
    const onGuardar = vi.fn().mockRejectedValueOnce(new Error("500"));
    render(
      <TablaEditable
        etiqueta="Fuentes"
        filas={filas}
        columnas={columnas}
        clave={(f) => f.id}
        onGuardar={onGuardar}
        errorFila={(f) => (f.id === "A" ? "El servidor no respondió." : null)}
      />,
    );
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Alfa 9" } });
    await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Guardar A" })); });
    expect(within(fila("A")).getByRole("button", { name: "Reintentar A" })).toHaveAccessibleDescription("El servidor no respondió.");
    expect(region()).toHaveTextContent("A: Error de guardado. El servidor no respondió.");
  });
});

// HU #14562 · M12 (auditoría): estado controlado opcional (`estadoFila`, según diseno.md §2.3) con rótulos propios,
// para que Carga muestre «Revalidando», «Corregida» e «Inválido» con la misma tabla. Sin la prop, nada cambia.
describe("TablaEditable: estado controlado (HU #14562 · M12)", () => {
  it("M12: estadoFila manda sobre el estado interno, con rótulo y tono propios", () => {
    render(
      <TablaEditable
        etiqueta="Fuentes"
        filas={filas}
        columnas={columnas}
        clave={(f) => f.id}
        onGuardar={vi.fn()}
        estadoFila={(f) =>
          f.id === "A" ? { estado: "guardando", texto: "Revalidando", tono: "info" } : { estado: "sin_cambios", texto: "Corregida", tono: "success" }
        }
      />,
    );
    expect(within(fila("A")).getByText("Revalidando")).toHaveAttribute("data-tono", "info");
    expect(within(fila("B")).getByText("Corregida")).toHaveAttribute("data-tono", "success");
    // Un «guardando» impuesto por el padre (sin edición propia) no ofrece Guardar ni Descartar y deja el campo de solo lectura.
    expect(within(fila("A")).queryByRole("button", { name: "Guardar A" })).toBeNull();
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveAttribute("readonly");
  });

  it("M12: con solo el nombre del estado usa el rótulo por defecto o el de rotulosEstado", () => {
    render(
      <TablaEditable
        etiqueta="Fuentes"
        filas={filas}
        columnas={columnas}
        clave={(f) => f.id}
        onGuardar={vi.fn()}
        estadoFila={(f) => (f.id === "A" ? "error" : null)}
        rotulosEstado={{ error: "Inválido" }}
      />,
    );
    expect(within(fila("A")).getByText("Inválido")).toBeInTheDocument();
    expect(within(fila("A")).getByRole("button", { name: "Reintentar A" })).toBeInTheDocument();
    // null → estado interno de la fila.
    expect(within(fila("B")).getByText("Sin cambios")).toBeInTheDocument();
  });

  it("M12: un estado controlado de error que llega mientras el usuario escribe no le mueve el foco", () => {
    const { rerender } = render(
      <TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} estadoFila={() => "sin_cambios"} />,
    );
    const campo = screen.getByRole("textbox", { name: "Nombre de A" });
    campo.focus();
    rerender(
      <TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} estadoFila={() => "error"} />,
    );
    expect(campo).toHaveFocus();
  });

  it("M12: el editor recibe la fila (borrador) como cuarto argumento", () => {
    const editor = vi.fn((valor: unknown, _onCambio: unknown, etiqueta: string) => <input aria-label={etiqueta} defaultValue={String(valor)} />);
    render(
      <TablaEditable
        etiqueta="Fuentes"
        filas={filas}
        columnas={[columnas[0], { ...columnas[1], editor }]}
        clave={(f) => f.id}
        onGuardar={vi.fn()}
      />,
    );
    expect(editor).toHaveBeenCalledWith("Alfa", expect.any(Function), "Nombre de A", filas[0]);
  });

  it("M12: si el padre cambia la fila y no hay edición pendiente, la tabla muestra el valor nuevo", () => {
    const { rerender } = render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    rerender(
      <TablaEditable
        etiqueta="Fuentes"
        filas={[{ ...filas[0], nombre: "Alfa recargada" }, filas[1]]}
        columnas={columnas}
        clave={(f) => f.id}
        onGuardar={vi.fn()}
      />,
    );
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Alfa recargada");
  });

  it("M12: con una edición pendiente, la recarga del padre no pisa lo digitado", () => {
    const { rerender } = render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Mío" } });
    rerender(
      <TablaEditable
        etiqueta="Fuentes"
        filas={[{ ...filas[0], nombre: "Alfa recargada" }, filas[1]]}
        columnas={columnas}
        clave={(f) => f.id}
        onGuardar={vi.fn()}
      />,
    );
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Mío");
  });
});

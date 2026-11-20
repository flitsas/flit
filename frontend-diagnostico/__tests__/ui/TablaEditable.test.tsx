import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_FILA_ANCHA_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";

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
  // HU #14562 · N3: la región viva no lleva aria-label (reemplazaría el texto anunciado); es la única de la tabla.
  const region = () => screen.getByRole("status");

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
    // Observación 3 del code review: el quinto argumento es `guardando` (false fuera de un guardado).
    expect(editor).toHaveBeenCalledWith("Alfa", expect.any(Function), "Nombre de A", filas[0], false);
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

// HU #14562 · M6 (AC56): por debajo de 900 px la tabla pasa a tarjetas con las recetas TABLA_MOVIL_* y cada celda dice su
// columna con data-label. En escritorio el marcado es el mismo de antes (solo se suman clases max-[900px]).
describe("TablaEditable: tarjetas por debajo de 900 px (HU #14562 · M6 · AC56)", () => {
  const clases = (el: Element) => (el.getAttribute("class") ?? "").split(/\s+/);
  const tiene = (el: Element, receta: string) => {
    for (const c of receta.split(" ")) expect(clases(el), c).toContain(c);
  };

  it("tabla, cabecera, cuerpo, filas y celdas llevan las recetas de tarjeta y cada celda su data-label", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    const tabla = screen.getByRole("table", { name: "Fuentes" });
    tiene(tabla, TABLA_MOVIL_TABLE_CLS);
    tiene(tabla.querySelector("thead")!, TABLA_MOVIL_THEAD_CLS);
    tiene(tabla.querySelector("tbody")!, TABLA_MOVIL_TBODY_CLS);
    for (const tr of tabla.querySelectorAll("tbody tr")) {
      tiene(tr, TABLA_MOVIL_ROW_CLS);
      const celdas = Array.from(tr.children);
      expect(celdas.map((c) => c.getAttribute("data-label"))).toEqual(["Código", "Nombre", "Límite", "Estado", "Acciones"]);
      for (const c of celdas) tiene(c, TABLA_MOVIL_CELL_CLS);
      tiene(celdas[0], TABLA_MOVIL_CELL_TITULO_CLS);
    }
  });

  it("la fila vacía ocupa la rejilla entera", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={[]} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} vacio="No hay fuentes" />);
    const celda = screen.getByText("No hay fuentes");
    tiene(celda, TABLA_MOVIL_FILA_ANCHA_CLS);
    tiene(celda.closest("tr")!, TABLA_MOVIL_FILA_ANCHA_CLS);
  });

  it("retrocompatible: la cabecera sigue siendo legible por lector de pantalla y las columnas conservan su nombre", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    for (const t of ["Código", "Nombre", "Límite", "Estado", "Acciones"]) expect(screen.getByRole("columnheader", { name: t })).toBeInTheDocument();
    expect(screen.getByRole("rowheader", { name: "A" })).toBeInTheDocument();
  });
});

// HU #14562 · N3 y observación 4 del code review: el nombre accesible de la fila sale de `nombreFila`, separado de la
// `clave` (que solo identifica la fila para React). La región viva no lleva aria-label y dos anuncios iguales seguidos se
// vuelven a anunciar.
describe("TablaEditable: nombreFila y región viva (HU #14562 · N3)", () => {
  it("N3: la región viva no tiene aria-label, así el lector lee su texto", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    expect(screen.getByRole("status")).not.toHaveAttribute("aria-label");
  });

  it("nombreFila da el nombre de la fila, de sus campos, de sus botones y de sus anuncios; la clave no se anuncia", () => {
    render(
      <TablaEditable
        etiqueta="Fuentes"
        filas={filas}
        columnas={columnas}
        clave={(f) => `id-${f.id}`}
        nombreFila={(f) => `Fila ${f.id}`}
        onGuardar={vi.fn()}
      />,
    );
    const a = screen.getByRole("row", { name: "Fila A" });
    fireEvent.change(within(a).getByRole("textbox", { name: "Nombre de Fila A" }), { target: { value: "Alfa 2" } });
    expect(within(a).getByRole("button", { name: "Guardar Fila A" })).toBeInTheDocument();
    expect(within(a).getByRole("button", { name: "Descartar Fila A" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Fila A: Editado");
    expect(screen.getByRole("status")).not.toHaveTextContent("id-A");
  });

  it("sin nombreFila se usa la clave, como antes", () => {
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    expect(screen.getByRole("row", { name: "A" })).toBeInTheDocument();
  });

  it("dos anuncios idénticos seguidos se repiten: un segundo fallo al reintentar se vuelve a anunciar", async () => {
    const onGuardar = vi.fn().mockRejectedValue(new Error("500"));
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
    const region = screen.getByRole("status");
    expect(region).toHaveTextContent("A: Error de guardado. El servidor no respondió.");
    const primero = region.firstElementChild;
    expect(primero).not.toBeNull();
    // Guardando y el nuevo error llegan en el mismo lote de render: el texto es el mismo, pero el nodo cambia.
    await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Reintentar A" })); });
    expect(onGuardar).toHaveBeenCalledTimes(2);
    expect(region).toHaveTextContent("A: Error de guardado. El servidor no respondió.");
    expect(region.firstElementChild).not.toBe(primero);
  });
});

// Observación 3 del code review: mientras la fila guarda, la tabla ignora los cambios del editor y le avisa con el quinto
// argumento (`guardando`) para que el control propio pueda quedar de solo lectura.
describe("TablaEditable: ediciones durante el guardado (code review, observación 3)", () => {
  it("mientras guarda, un cambio del editor no toca el borrador y el editor recibe guardando=true", async () => {
    let resolver!: () => void;
    const onGuardar = vi.fn(() => new Promise<void>((r) => { resolver = r; }));
    const editor = vi.fn(
      (valor: unknown, onCambio: (v: string) => void, etiqueta: string, _fila: Fila, guardando?: boolean) => (
        <input aria-label={etiqueta} data-guardando={guardando ? "si" : "no"} value={String(valor)} onChange={(e) => onCambio(e.target.value)} />
      ),
    );
    render(
      <TablaEditable
        etiqueta="Fuentes"
        filas={filas}
        columnas={[columnas[0], { ...columnas[1], editor: editor as never }, columnas[2]]}
        clave={(f) => f.id}
        onGuardar={onGuardar}
      />,
    );
    const campo = screen.getByRole("textbox", { name: "Nombre de A" });
    expect(campo).toHaveAttribute("data-guardando", "no");
    fireEvent.change(campo, { target: { value: "Alfa 2" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar A" }));
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveAttribute("data-guardando", "si");
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Durante" } });
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Alfa 2");
    expect(onGuardar).toHaveBeenCalledWith({ id: "A", nombre: "Alfa 2", limite: "10" });
    await act(async () => { resolver(); });
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Alfa 2");
  });

  it("mientras guarda, el campo de texto por defecto también ignora lo que llegue", async () => {
    let resolver!: () => void;
    const onGuardar = vi.fn(() => new Promise<void>((r) => { resolver = r; }));
    render(<TablaEditable etiqueta="Fuentes" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(screen.getByRole("textbox", { name: "Límite de A" }), { target: { value: "11" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar A" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Nombre de A" }), { target: { value: "Durante" } });
    expect(screen.getByRole("textbox", { name: "Nombre de A" })).toHaveValue("Alfa");
    await act(async () => { resolver(); });
  });
});

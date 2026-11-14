import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { TablaEditable, type ColumnaEditable } from "@flit/ui/TablaEditable";

// Uso de ejemplo (saneamiento PS-5, spec-ui §8): <TablaEditable guardado="automatico" etiqueta="Conceptos a sanear"
//   filas={renglones} clave={(r) => r.id} columnas={[{ campo: "resultado", titulo: "Resultado", editable: true,
//   guardarAlCambiarMs: 600, editor }, { campo: "nota", titulo: "Nota", editable: true }]} onGuardar={guardarRenglon} />
// Sin Guardar ni Descartar: la nota guarda al salir del campo y el resultado 600 ms después del último cambio.

interface Fila { id: string; nota: string; resultado: string; version: number }
const filas: Fila[] = [
  { id: "A", nota: "", resultado: "PENDIENTE", version: 1 },
  { id: "B", nota: "", resultado: "PENDIENTE", version: 1 },
];

/** Selector simple: un botón que pasa al siguiente resultado (como una flecha en el SelectorSegmentado). */
const ORDEN = ["PENDIENTE", "SANEADO", "NO_PROCEDE"];
const columnas: ColumnaEditable<Fila>[] = [
  { campo: "id", titulo: "Concepto" },
  {
    campo: "resultado",
    titulo: "Resultado",
    editable: true,
    guardarAlCambiarMs: 600,
    editor: (valor, onCambio, etiqueta) => (
      <button type="button" aria-label={etiqueta} onClick={() => onCambio(ORDEN[(ORDEN.indexOf(String(valor)) + 1) % 3])}>
        {String(valor)}
      </button>
    ),
  },
  { campo: "nota", titulo: "Nota", editable: true },
];

const ROTULOS = { editado: "Sin guardar", guardando: "Guardando…", error: "No se guardó" } as const;

const fila = (id: string) => screen.getByRole("row", { name: id });
const nota = (id: string) => screen.getByRole("textbox", { name: `Nota de ${id}` });
const estadoDe = (id: string) => within(fila(id)).getByText(/Sin cambios|Sin guardar|Guardando…|Guardado|No se guardó/);

afterEach(() => vi.useRealTimers());

describe("TablaEditable · guardado automático (saneamiento, spec-ui §8)", () => {
  it("guarda la nota al salir del campo, sin botones Guardar ni Descartar", async () => {
    const onGuardar = vi.fn().mockResolvedValue(undefined);
    render(<TablaEditable guardado="automatico" rotulosEstado={ROTULOS} etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(nota("A"), { target: { value: "Pagado en ventanilla" } });
    expect(estadoDe("A")).toHaveTextContent("Sin guardar");
    expect(within(fila("A")).queryByRole("button", { name: "Guardar A" })).toBeNull();
    expect(within(fila("A")).queryByRole("button", { name: "Descartar A" })).toBeNull();
    expect(onGuardar).not.toHaveBeenCalled();

    await act(async () => {
      fireEvent.blur(nota("A"), { relatedTarget: nota("B") });
    });
    expect(onGuardar).toHaveBeenCalledTimes(1);
    expect(onGuardar).toHaveBeenCalledWith({ id: "A", nota: "Pagado en ventanilla", resultado: "PENDIENTE", version: 1 });
  });

  it("tras guardar muestra «Guardado» en tono success hasta el siguiente cambio y no mueve el foco", async () => {
    const onGuardar = vi.fn().mockResolvedValue(undefined);
    render(<TablaEditable guardado="automatico" rotulosEstado={ROTULOS} etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(nota("A"), { target: { value: "x" } });
    nota("B").focus();
    await act(async () => {
      fireEvent.blur(nota("A"), { relatedTarget: nota("B") });
    });
    const estado = estadoDe("A");
    expect(estado).toHaveTextContent("Guardado");
    expect(estado).toHaveAttribute("data-tono", "success");
    expect(document.activeElement).toBe(nota("B"));
    expect(screen.getByRole("status")).toHaveTextContent("A: Guardado");

    fireEvent.change(nota("A"), { target: { value: "xy" } });
    expect(estadoDe("A")).toHaveTextContent("Sin guardar");
  });

  it("acepta un rótulo propio para el guardado (rotuloGuardado)", async () => {
    render(
      <TablaEditable guardado="automatico" rotuloGuardado="Listo" etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn().mockResolvedValue(undefined)} />,
    );
    fireEvent.change(nota("A"), { target: { value: "x" } });
    await act(async () => {
      fireEvent.blur(nota("A"), { relatedTarget: null });
    });
    expect(within(fila("A")).getByText("Listo")).toHaveAttribute("data-tono", "success");
  });

  it("no guarda al salir del campo sin cambios", () => {
    const onGuardar = vi.fn();
    render(<TablaEditable guardado="automatico" etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.blur(nota("A"), { relatedTarget: nota("B") });
    expect(onGuardar).not.toHaveBeenCalled();
  });

  it("no guarda si el foco se mueve dentro de la misma celda", () => {
    const onGuardar = vi.fn();
    render(<TablaEditable guardado="automatico" etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(nota("A"), { target: { value: "x" } });
    fireEvent.blur(nota("A"), { relatedTarget: nota("A") });
    expect(onGuardar).not.toHaveBeenCalled();
  });

  it("el selector guarda 600 ms después del último cambio, una sola vez tras tres flechas", async () => {
    vi.useFakeTimers();
    const onGuardar = vi.fn().mockResolvedValue(undefined);
    render(<TablaEditable guardado="automatico" etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    const selector = screen.getByRole("button", { name: "Resultado de A" });
    fireEvent.click(selector);
    act(() => vi.advanceTimersByTime(200));
    fireEvent.click(selector);
    act(() => vi.advanceTimersByTime(200));
    fireEvent.click(selector);
    act(() => vi.advanceTimersByTime(599));
    expect(onGuardar).not.toHaveBeenCalled();
    await act(async () => {
      vi.advanceTimersByTime(1);
    });
    expect(onGuardar).toHaveBeenCalledTimes(1);
    expect(onGuardar.mock.calls[0][0]).toMatchObject({ id: "A", resultado: "PENDIENTE" });
  });

  it("salir del grupo adelanta el guardado del selector y cancela el temporizador", async () => {
    vi.useFakeTimers();
    const onGuardar = vi.fn().mockResolvedValue(undefined);
    render(<TablaEditable guardado="automatico" etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    const selector = screen.getByRole("button", { name: "Resultado de A" });
    fireEvent.click(selector);
    await act(async () => {
      fireEvent.blur(selector, { relatedTarget: nota("A") });
    });
    expect(onGuardar).toHaveBeenCalledTimes(1);
    expect(onGuardar.mock.calls[0][0]).toMatchObject({ resultado: "SANEADO" });
    await act(async () => {
      vi.advanceTimersByTime(1000);
    });
    expect(onGuardar).toHaveBeenCalledTimes(1);
  });

  it("un cambio que llega mientras guarda se envía al terminar, con la versión nueva de la fila", async () => {
    vi.useFakeTimers();
    const resolvers: Array<() => void> = [];
    const enviados: Fila[] = [];
    function Contenedor() {
      const [datos, setDatos] = useState(filas);
      return (
        <TablaEditable
          guardado="automatico"
          etiqueta="Conceptos"
          filas={datos}
          columnas={columnas}
          clave={(f) => f.id}
          onGuardar={(f) =>
            new Promise<void>((r) => {
              enviados.push(f);
              resolvers.push(() => {
                setDatos((d) => d.map((x) => (x.id === f.id ? { ...f, version: f.version + 1 } : x)));
                r();
              });
            })
          }
        />
      );
    }
    render(<Contenedor />);
    const selector = screen.getByRole("button", { name: "Resultado de A" });
    fireEvent.click(selector);
    await act(async () => {
      vi.advanceTimersByTime(600);
    });
    expect(enviados).toHaveLength(1);
    fireEvent.click(screen.getByRole("button", { name: "Resultado de A" }));
    await act(async () => {
      vi.advanceTimersByTime(600);
    });
    expect(enviados).toHaveLength(1);
    await act(async () => {
      resolvers[0]();
    });
    await act(async () => {
      vi.advanceTimersByTime(0);
    });
    expect(enviados).toHaveLength(2);
    expect(enviados[1]).toMatchObject({ id: "A", resultado: "NO_PROCEDE", version: 2 });
  });

  it("si falla, deja «No se guardó» con Reintentar enfocado, conserva el borrador y reintenta", async () => {
    const onGuardar = vi.fn().mockRejectedValueOnce(new Error("red")).mockResolvedValueOnce(undefined);
    render(
      <TablaEditable
        guardado="automatico"
        rotulosEstado={ROTULOS}
        etiqueta="Conceptos"
        filas={filas}
        columnas={columnas}
        clave={(f) => f.id}
        onGuardar={onGuardar}
        errorFila={(f) => (f.id === "A" ? "Revisa tu conexión e intenta de nuevo." : null)}
      />,
    );
    fireEvent.change(nota("A"), { target: { value: "Nota digitada" } });
    const selector = screen.getByRole("button", { name: "Resultado de A" });
    selector.focus();
    await act(async () => {
      fireEvent.blur(nota("A"), { relatedTarget: selector });
    });
    expect(estadoDe("A")).toHaveTextContent("No se guardó");
    const reintentar = within(fila("A")).getByRole("button", { name: "Reintentar A" });
    expect(document.activeElement).toBe(reintentar);
    expect(nota("A")).toHaveValue("Nota digitada");
    await act(async () => {
      fireEvent.click(reintentar);
    });
    expect(onGuardar).toHaveBeenCalledTimes(2);
    expect(onGuardar.mock.calls[1][0]).toMatchObject({ nota: "Nota digitada" });
  });

  it("D-69: si falla y el padre repinta la fila con un objeto nuevo y la misma id, lo digitado se conserva y Reintentar lo envía", async () => {
    const enviados: Fila[] = [];
    let fallar = true;
    function Contenedor() {
      const [datos, setDatos] = useState(filas);
      return (
        <>
        <button type="button" onClick={() => setDatos((d) => d.map((x) => ({ ...x })))}>
          Repintar
        </button>
        <TablaEditable
          guardado="automatico"
          rotulosEstado={ROTULOS}
          etiqueta="Conceptos"
          filas={datos}
          columnas={columnas}
          clave={(f) => f.id}
          onGuardar={async (f) => {
            enviados.push(f);
            if (fallar) {
              // El padre repinta (p. ej. marca el error del renglón): objetos fila nuevos con la misma id.
              setDatos((d) => d.map((x) => ({ ...x })));
              throw new Error("red");
            }
          }}
        />
        </>
      );
    }
    render(<Contenedor />);
    fireEvent.change(nota("A"), { target: { value: "Nota digitada" } });
    await act(async () => {
      fireEvent.blur(nota("A"), { relatedTarget: nota("B") });
    });
    expect(enviados).toHaveLength(1);
    expect(estadoDe("A")).toHaveTextContent("No se guardó");
    expect(nota("A")).toHaveValue("Nota digitada");

    // Segundo repintado del padre con otra identidad, ya en error: lo digitado sigue ahí.
    fireEvent.click(screen.getByRole("button", { name: "Repintar" }));
    expect(nota("A")).toHaveValue("Nota digitada");
    expect(estadoDe("A")).toHaveTextContent("No se guardó");
    fallar = false;
    const reintentar = within(fila("A")).getByRole("button", { name: "Reintentar A" });
    await act(async () => {
      fireEvent.click(reintentar);
    });
    expect(enviados).toHaveLength(2);
    expect(enviados[1]).toMatchObject({ id: "A", nota: "Nota digitada" });
  });

  it("D-69: con el padre repintando en cada intento, Reintentar guarda lo digitado y la fila queda en «Guardado»", async () => {
    const enviados: Fila[] = [];
    let fallar = true;
    function Contenedor() {
      const [datos, setDatos] = useState(filas);
      return (
        <TablaEditable
          guardado="automatico"
          rotulosEstado={ROTULOS}
          etiqueta="Conceptos"
          filas={datos}
          columnas={columnas}
          clave={(f) => f.id}
          onGuardar={async (f) => {
            enviados.push(f);
            setDatos((d) => d.map((x) => ({ ...x })));
            if (fallar) throw new Error("red");
          }}
        />
      );
    }
    render(<Contenedor />);
    fireEvent.change(nota("A"), { target: { value: "Primera" } });
    await act(async () => {
      fireEvent.blur(nota("A"), { relatedTarget: nota("B") });
    });
    expect(nota("A")).toHaveValue("Primera");
    fallar = false;
    await act(async () => {
      fireEvent.click(within(fila("A")).getByRole("button", { name: "Reintentar A" }));
    });
    expect(enviados[1]).toMatchObject({ id: "A", nota: "Primera" });
    expect(estadoDe("A")).toHaveTextContent("Guardado");
    expect(nota("A")).toHaveValue("Primera");
  });

  it("la columna Acciones se pinta siempre, vacía si no hay error", () => {
    render(<TablaEditable guardado="automatico" etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={vi.fn()} />);
    expect(screen.getByRole("columnheader", { name: "Acciones" })).toBeInTheDocument();
    fireEvent.change(nota("A"), { target: { value: "x" } });
    expect(within(fila("A")).queryAllByRole("button").map((b) => b.getAttribute("aria-label"))).toEqual(["Resultado de A"]);
  });

  it("en el modo por defecto salir del campo no guarda (botón Guardar como siempre)", () => {
    const onGuardar = vi.fn();
    render(<TablaEditable etiqueta="Conceptos" filas={filas} columnas={columnas} clave={(f) => f.id} onGuardar={onGuardar} />);
    fireEvent.change(nota("A"), { target: { value: "x" } });
    fireEvent.blur(nota("A"), { relatedTarget: nota("B") });
    expect(onGuardar).not.toHaveBeenCalled();
    expect(within(fila("A")).getByRole("button", { name: "Guardar A" })).toBeInTheDocument();
  });
});

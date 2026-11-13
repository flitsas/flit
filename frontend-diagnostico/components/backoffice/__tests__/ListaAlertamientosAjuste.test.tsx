import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ListaAlertamientosAjuste } from "@/components/backoffice/ListaAlertamientosAjuste";
import type { FilaAlertamiento } from "@/lib/backoffice/ajusteDictamen";

// Uso de ejemplo: <ListaAlertamientosAjuste alertamientos={filas} onCambiar={setFilas} errores={erroresApi} /> dentro del
// panel «Cambiar veredicto»: TablaEditable con guardado por fila (texto, efecto y fuente), «Agregar alertamiento»
// (AJUSTE_FLIT) y «Eliminar» por fila; los errores del API por campo (P3) se muestran junto a la tabla.

const SOAT: FilaAlertamiento = { clave: "Alertamiento 1", codigo: "SOAT_NO_VIGENTE", texto: "El SOAT no está vigente.", efecto: "BLOQUEA", fuente: "RUNT" };
const PRENDA: FilaAlertamiento = { clave: "Alertamiento 2", codigo: "PRENDA_VIGENTE", texto: "Tiene prenda vigente.", efecto: "ADVIERTE", fuente: "RUNT" };

const montar = (filas: FilaAlertamiento[] = [SOAT, PRENDA], extra: Partial<Parameters<typeof ListaAlertamientosAjuste>[0]> = {}) => {
  const onCambiar = vi.fn();
  render(<ListaAlertamientosAjuste alertamientos={filas} onCambiar={onCambiar} {...extra} />);
  return onCambiar;
};

describe("HU14562 AC45 lista editable de alertamientos", () => {
  it("es una TablaEditable con una fila por alertamiento y su estado «Sin cambios»", () => {
    montar();
    const tabla = screen.getByRole("table", { name: "Alertamientos del ajuste" });
    expect(within(tabla).getAllByRole("row")).toHaveLength(3);
    expect(within(tabla).getAllByText("Sin cambios")).toHaveLength(2);
  });

  it("editar el texto marca la fila «Editado» y «Guardar» la devuelve con ajuste EDITADO", async () => {
    const onCambiar = montar();
    fireEvent.change(screen.getByRole("textbox", { name: "Texto de Alertamiento 1" }), { target: { value: "SOAT vencido hace 3 días." } });
    expect(screen.getByText("Editado")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Guardar Alertamiento 1" }));
    await waitFor(() => expect(onCambiar).toHaveBeenCalledWith([{ ...SOAT, texto: "SOAT vencido hace 3 días.", ajuste: "EDITADO" }, PRENDA]));
  });

  it("el efecto y la fuente se editan con listas", async () => {
    const onCambiar = montar();
    fireEvent.change(screen.getByRole("combobox", { name: "Efecto de Alertamiento 2" }), { target: { value: "BLOQUEA" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar Alertamiento 2" }));
    await waitFor(() => expect(onCambiar).toHaveBeenCalledWith([SOAT, { ...PRENDA, efecto: "BLOQUEA", ajuste: "EDITADO" }]));
    expect(screen.getByRole("combobox", { name: "Fuente de Alertamiento 2" })).toHaveValue("RUNT");
  });

  it("«Agregar alertamiento» suma uno libre AJUSTE_FLIT y «Eliminar» lo quita", () => {
    const onCambiar = montar();
    fireEvent.click(screen.getByRole("button", { name: "Agregar alertamiento" }));
    expect(onCambiar).toHaveBeenLastCalledWith([
      SOAT,
      PRENDA,
      { clave: "Alertamiento 3", codigo: "AJUSTE_FLIT", texto: "", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" },
    ]);
    fireEvent.click(screen.getByRole("button", { name: "Eliminar Alertamiento 1" }));
    expect(onCambiar).toHaveBeenLastCalledWith([PRENDA]);
  });

  it("P3: guardar un alertamiento libre con 6 o más dígitos falla en la fila con su mensaje", async () => {
    const libre: FilaAlertamiento = { clave: "Alertamiento 3", codigo: "AJUSTE_FLIT", texto: "", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" };
    const onCambiar = montar([libre]);
    fireEvent.change(screen.getByRole("textbox", { name: "Texto de Alertamiento 3" }), { target: { value: "Radicado 1234567" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar Alertamiento 3" }));
    expect(await screen.findByText("Error de guardado")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Alertamiento 3: No escribas secuencias de 6 o más dígitos (pueden ser documentos).");
    expect(onCambiar).not.toHaveBeenCalled();
  });

  it("un 422 del API sobre alertamientos[i].texto se muestra junto a la fila correspondiente", () => {
    montar([SOAT, PRENDA], { errores: { "alertamientos[1].texto": "El texto no puede llevar secuencias de 6 o más dígitos" } });
    expect(screen.getByRole("alert")).toHaveTextContent("Alertamiento 2: El texto no puede llevar secuencias de 6 o más dígitos");
  });

  it("sin alertamientos muestra el vacío", () => {
    montar([]);
    expect(screen.getByText("Sin alertamientos. Un dictamen Bloqueado necesita al menos uno.")).toBeInTheDocument();
  });
});

import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { ListaAlertamientosAjuste } from "@/components/backoffice/ListaAlertamientosAjuste";
import type { FilaAlertamiento } from "@/lib/backoffice/ajusteDictamen";

// Uso de ejemplo: <ListaAlertamientosAjuste alertamientos={filas} onCambiar={setFilas} errores={erroresApi} /> dentro del
// panel «Cambiar veredicto»: una tarjeta por alertamiento con texto, efecto y fuente que se editan en línea (sin guardado por
// fila: el guardado es «Aplicar» del panel, HU #14562 auditoría M8), «Agregar alertamiento» (AJUSTE_FLIT) y «Eliminar». Cada
// error va junto a su campo con aria-invalid y aria-describedby (AC56).

const SOAT: FilaAlertamiento = { clave: "Alertamiento 1", codigo: "SOAT_NO_VIGENTE", texto: "El SOAT no está vigente.", efecto: "BLOQUEA", fuente: "RUNT" };
const PRENDA: FilaAlertamiento = { clave: "Alertamiento 2", codigo: "PRENDA_VIGENTE", texto: "Tiene prenda vigente.", efecto: "ADVIERTE", fuente: "RUNT" };

const montar = (filas: FilaAlertamiento[] = [SOAT, PRENDA], extra: Partial<Parameters<typeof ListaAlertamientosAjuste>[0]> = {}) => {
  const onCambiar = vi.fn();
  render(<ListaAlertamientosAjuste alertamientos={filas} onCambiar={onCambiar} {...extra} />);
  return onCambiar;
};

/** Con estado real, como en el panel: lo que se escribe queda en la lista sin pasos extra. */
function Controlada({ inicial, onCambio }: { inicial: FilaAlertamiento[]; onCambio: (l: FilaAlertamiento[]) => void }) {
  const [lista, setLista] = useState(inicial);
  return (
    <ListaAlertamientosAjuste
      alertamientos={lista}
      onCambiar={(l) => {
        setLista(l);
        onCambio(l);
      }}
    />
  );
}

const descripcion = (control: HTMLElement) =>
  (control.getAttribute("aria-describedby") ?? "")
    .split(/\s+/)
    .filter(Boolean)
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" ");

describe("HU14562 AC45 / M8 lista de alertamientos del ajuste", () => {
  it("una tarjeta por alertamiento (lista con su nombre), sin tabla que desplace en el panel de 640 px", () => {
    montar();
    expect(screen.queryByRole("table")).toBeNull();
    const lista = screen.getByRole("list", { name: "Alertamientos del ajuste" });
    const items = within(lista).getAllByRole("listitem");
    expect(items).toHaveLength(2);
    expect(within(items[0]).getByRole("group", { name: /Alertamiento 1 · SOAT no vigente/ })).toBeInTheDocument();
  });

  it("M8: editar el texto llega a la lista al instante, con ajuste EDITADO, sin «Guardar» por fila", () => {
    const onCambiar = montar();
    expect(screen.queryByRole("button", { name: /^Guardar/ })).toBeNull();
    fireEvent.change(screen.getByRole("textbox", { name: "Texto de Alertamiento 1" }), { target: { value: "SOAT vencido hace 3 días." } });
    expect(onCambiar).toHaveBeenLastCalledWith([{ ...SOAT, texto: "SOAT vencido hace 3 días.", ajuste: "EDITADO" }, PRENDA]);
  });

  it("M8: el efecto y la fuente se editan con listas y también llegan al instante", () => {
    const onCambio = vi.fn();
    render(<Controlada inicial={[SOAT, PRENDA]} onCambio={onCambio} />);
    fireEvent.change(screen.getByRole("combobox", { name: "Efecto de Alertamiento 2" }), { target: { value: "BLOQUEA" } });
    fireEvent.change(screen.getByRole("combobox", { name: "Fuente de Alertamiento 2" }), { target: { value: "SIMIT_VEHICULO" } });
    expect(onCambio).toHaveBeenLastCalledWith([SOAT, { ...PRENDA, efecto: "BLOQUEA", fuente: "SIMIT_VEHICULO", ajuste: "EDITADO" }]);
    expect(screen.getByRole("combobox", { name: "Fuente de Alertamiento 2" })).toHaveValue("SIMIT_VEHICULO");
  });

  it("un alertamiento agregado conserva la marca AGREGADO al editarlo", () => {
    const libre: FilaAlertamiento = { clave: "Alertamiento 3", codigo: "AJUSTE_FLIT", texto: "", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" };
    const onCambiar = montar([libre]);
    fireEvent.change(screen.getByRole("textbox", { name: "Texto de Alertamiento 3" }), { target: { value: "Revisión técnica pendiente" } });
    expect(onCambiar).toHaveBeenLastCalledWith([{ ...libre, texto: "Revisión técnica pendiente" }]);
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

  it("los campos no nacen inválidos: el error aparece cuando el usuario deja el texto mal escrito", () => {
    const libre: FilaAlertamiento = { clave: "Alertamiento 3", codigo: "AJUSTE_FLIT", texto: "", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" };
    render(<Controlada inicial={[libre]} onCambio={vi.fn()} />);
    const texto = screen.getByRole("textbox", { name: "Texto de Alertamiento 3" });
    expect(texto).not.toHaveAttribute("aria-invalid");
    fireEvent.change(texto, { target: { value: "Radicado 1234567" } });
    fireEvent.blur(texto);
    expect(texto).toHaveAttribute("aria-invalid", "true");
    expect(descripcion(texto)).toContain("No escribas secuencias de 6 o más dígitos (pueden ser documentos).");
  });

  it("AC56: un 422 del API sobre alertamientos[i].texto queda asociado a ese campo con aria-describedby", () => {
    montar([SOAT, PRENDA], { errores: { "alertamientos[1].texto": "El texto no puede llevar secuencias de 6 o más dígitos" } });
    const texto = screen.getByRole("textbox", { name: "Texto de Alertamiento 2" });
    expect(texto).toHaveAttribute("aria-invalid", "true");
    expect(descripcion(texto)).toContain("El texto no puede llevar secuencias de 6 o más dígitos");
    expect(screen.getByRole("textbox", { name: "Texto de Alertamiento 1" })).not.toHaveAttribute("aria-invalid");
  });

  it("sin alertamientos muestra el vacío", () => {
    montar([]);
    expect(screen.getByText("Sin alertamientos. Un dictamen Bloqueado necesita al menos uno.")).toBeInTheDocument();
  });
});

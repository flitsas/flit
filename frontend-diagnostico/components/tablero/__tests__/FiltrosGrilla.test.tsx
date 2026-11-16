import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { GrillaLote, type GrillaLoteProps } from "@/components/tablero/GrillaLote";
import { V1, V2, V3, celda, grilla, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <GrillaLote grilla={grilla} filtro={tarjeta} onQuitarFiltro={() => setTarjeta(null)} … /> → encima de la tabla
// la barra «Buscar por placa o VIN» · Fuente · Estado · Dictamen · Cambio de propietario (n) · Limpiar filtros, que filtra en
// memoria (Y). HU #14584 (spec-ui §6.6, A6.8; contrato §2: sin parámetros nuevos en /grilla; SP-21: solo códigos).

const V4 = vehiculo(4, [celda(4, "IMPUESTO_DEPARTAMENTAL", "COMPLETADO", { tiene_deuda: true }), celda(4, "SIMIT_PROPIETARIO", "COMPLETADO")], {
  veredicto: "BLOQUEADO",
  cambio_propietario: true,
});
const V5 = vehiculo(5, [celda(5, "SIMIT_PROPIETARIO", "ERROR")], { veredicto: "VIABLE", cambio_propietario: false });

const pintar = (props: Partial<GrillaLoteProps> = {}) =>
  render(
    <GrillaLote
      grilla={grilla({ estado: "COMPLETADO", vehiculos: [V1, V2, V3, V4, V5] })}
      filtro={null}
      puedeReprocesar={false}
      onReprocesada={vi.fn()}
      onErrorReproceso={vi.fn()}
      tituloAccionVehiculo="Acciones"
      {...props}
    />,
  );

const filasVisibles = () =>
  within(screen.getByRole("table", { name: /semáforo de consultas/i }))
    .getAllByRole("rowheader")
    .map((th) => th.textContent?.match(/TST\d{3}/)?.[0]);
const fuente = () => screen.getByRole("combobox", { name: "Fuente" });
const estado = () => screen.getByRole("combobox", { name: "Estado" });

describe("FiltrosGrilla — HU #14584 §6.6: barra de filtros en memoria", () => {
  it("Fuente ofrece «Todas» y solo las fuentes pedidas por el lote", () => {
    pintar();
    const opciones = within(fuente()).getAllByRole("option").map((o) => o.textContent);
    expect(opciones).toEqual(["Todas", "RUNT vehículo", "RUNT locatario", "SIMIT propietario", "Impuesto departamental"]);
  });

  it("Estado está deshabilitado hasta elegir una fuente y lo explica con aria-describedby", () => {
    pintar();
    expect(estado()).toBeDisabled();
    const ayuda = document.getElementById(estado().getAttribute("aria-describedby") ?? "");
    expect(ayuda).toHaveTextContent("Elige primero una fuente");
  });

  it("A6.8: SIMIT propietario = Error deja solo esas filas; el estado muestra los presentes con su conteo", () => {
    pintar();
    fireEvent.change(fuente(), { target: { value: "SIMIT_PROPIETARIO" } });
    expect(estado()).toBeEnabled();
    expect(within(estado()).getAllByRole("option").map((o) => o.textContent)).toEqual(["Todos", "Ejecutado (2)", "Error (2)", "Sin ejecutar (1)"]);
    fireEvent.change(estado(), { target: { value: "ERROR" } });
    expect(filasVisibles()).toEqual(["TST002", "TST005"]);
  });

  it("impuesto = Con deuda deja la fila con deuda confirmada", () => {
    pintar();
    fireEvent.change(fuente(), { target: { value: "IMPUESTO_DEPARTAMENTAL" } });
    fireEvent.change(estado(), { target: { value: "CON_DEUDA" } });
    expect(filasVisibles()).toEqual(["TST004"]);
  });

  it("cambiar de fuente reinicia el estado", () => {
    pintar();
    fireEvent.change(fuente(), { target: { value: "SIMIT_PROPIETARIO" } });
    fireEvent.change(estado(), { target: { value: "ERROR" } });
    fireEvent.change(fuente(), { target: { value: "RUNT" } });
    expect(estado()).toHaveValue("");
    expect(filasVisibles()).toHaveLength(5);
  });

  it("Dictamen: Todos / Viable / Bloqueado / Pendiente, solo si alguna fila trae veredicto", () => {
    pintar();
    const dictamen = screen.getByRole("combobox", { name: "Dictamen" });
    expect(within(dictamen).getAllByRole("option").map((o) => o.textContent)).toEqual(["Todos", "Viable", "Bloqueado", "Pendiente"]);
    fireEvent.change(dictamen, { target: { value: "BLOQUEADO" } });
    expect(filasVisibles()).toEqual(["TST004"]);
  });

  it("sin veredictos ni cambio de propietario conocido no hay filtro de Dictamen ni chip de cambio", () => {
    pintar({ grilla: grilla({ estado: "COMPLETADO" }) });
    expect(screen.queryByRole("combobox", { name: "Dictamen" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Cambio de propietario \(/ })).toBeNull();
  });

  it("chip «Cambio de propietario (n)» con aria-pressed deja solo las filas con true", () => {
    pintar();
    const chip = screen.getByRole("button", { name: "Cambio de propietario (1)" });
    expect(chip).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(chip);
    expect(chip).toHaveAttribute("aria-pressed", "true");
    expect(filasVisibles()).toEqual(["TST004"]);
  });

  it("«Limpiar filtros» siempre visible: deshabilitado sin filtros; limpia todo, apaga la tarjeta y lleva el foco al buscador", () => {
    const onQuitarFiltro = vi.fn();
    pintar({ onQuitarFiltro });
    const limpiar = screen.getByRole("button", { name: "Limpiar filtros" });
    expect(limpiar).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Buscar por placa o VIN"), { target: { value: "TST00" } });
    fireEvent.change(fuente(), { target: { value: "SIMIT_PROPIETARIO" } });
    fireEvent.change(estado(), { target: { value: "ERROR" } });
    fireEvent.click(screen.getByRole("button", { name: "Cambio de propietario (1)" }));
    expect(limpiar).toBeEnabled();
    fireEvent.click(limpiar);
    expect(screen.getByLabelText("Buscar por placa o VIN")).toHaveValue("");
    expect(fuente()).toHaveValue("");
    expect(estado()).toHaveValue("");
    expect(screen.getByRole("button", { name: "Cambio de propietario (1)" })).toHaveAttribute("aria-pressed", "false");
    expect(onQuitarFiltro).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText("Buscar por placa o VIN")).toHaveFocus();
    expect(filasVisibles()).toHaveLength(5);
  });

  it("con una tarjeta-filtro activa (métrica) «Limpiar filtros» está habilitado y la apaga", () => {
    const onQuitarFiltro = vi.fn();
    pintar({ filtro: "bloqueados", onQuitarFiltro });
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" })[0]);
    expect(onQuitarFiltro).toHaveBeenCalledTimes(1);
  });

  it("el contador sigue en aria-live y cuenta las filas filtradas", () => {
    pintar();
    fireEvent.change(screen.getByRole("combobox", { name: "Dictamen" }), { target: { value: "VIABLE" } });
    const contador = document.querySelector('[aria-live="polite"]')!;
    expect(contador).toHaveTextContent("1 de 5");
  });

  it("SP-21: los filtros no escriben en la URL ni en localStorage", () => {
    const antes = window.location.href;
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    pintar();
    fireEvent.change(fuente(), { target: { value: "SIMIT_PROPIETARIO" } });
    fireEvent.click(screen.getByRole("button", { name: "Cambio de propietario (1)" }));
    expect(window.location.href).toBe(antes);
    expect(setItem).not.toHaveBeenCalled();
    setItem.mockRestore();
  });
});

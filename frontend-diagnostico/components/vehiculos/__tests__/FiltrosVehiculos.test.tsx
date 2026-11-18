import { readFileSync } from "node:fs";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { ESPERA_PLACA_MS, FiltrosVehiculos, type FiltrosVehiculosProps } from "@/components/vehiculos/FiltrosVehiculos";
import { filtrosVehiculosDesdeUrl, type FiltrosVehiculos as Filtros } from "@/lib/vehiculos/listado";

// Uso de ejemplo (HU #14577 AC21, spec-ui §4.2): filtros de la pestaña «Vehículos» del inicio, en una tarjeta.
//   <FiltrosVehiculos valor={filtros} error={errorCampo} onCambiar={aplicar} onLimpiar={limpiar} onActualizar={recargar} />
// «Placa» espera 400 ms o Enter; «Estado del vehículo», «Dictamen», «Saneamiento», «Modo» y «Trámite» son <select> nativos de
// una opción (D-10); «Desde» y «Hasta» son fechas. Un 400 con `campo` deja ese control inválido con el mensaje asociado.

const vacios = (cambios: Partial<Filtros> = {}): Filtros => ({ ...filtrosVehiculosDesdeUrl(new URLSearchParams()), ...cambios });

function pintar(props: Partial<FiltrosVehiculosProps> = {}) {
  const onCambiar = vi.fn();
  const onLimpiar = vi.fn();
  const onActualizar = vi.fn();
  const r = render(<FiltrosVehiculos valor={vacios()} error={null} onCambiar={onCambiar} onLimpiar={onLimpiar} onActualizar={onActualizar} {...props} />);
  return { ...r, onCambiar, onLimpiar, onActualizar };
}

const opciones = (nombre: string) => within(screen.getByRole("combobox", { name: nombre })).getAllByRole("option").map((o) => o.textContent);

beforeEach(() => vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] }));
afterEach(() => vi.useRealTimers());

describe("FiltrosVehiculos — AC21: controles con etiqueta visible", () => {
  it("un grupo con nombre y cada control con su etiqueta: Placa, Estado del vehículo, Dictamen, Saneamiento, Modo, Trámite, Desde y Hasta", () => {
    pintar();
    const grupo = screen.getByRole("group", { name: "Filtros de vehículos" });
    expect(within(grupo).getByRole("textbox", { name: "Placa" })).toHaveAttribute("inputmode", "text");
    for (const nombre of ["Estado del vehículo", "Dictamen", "Saneamiento", "Modo", "Trámite"]) expect(within(grupo).getByRole("combobox", { name: nombre })).toBeInTheDocument();
    expect(within(grupo).getByLabelText("Desde")).toHaveAttribute("type", "date");
    expect(within(grupo).getByLabelText("Hasta")).toHaveAttribute("type", "date");
    expect(within(grupo).getByRole("button", { name: "Limpiar filtros" })).toBeInTheDocument();
    expect(within(grupo).getByRole("button", { name: "Actualizar" })).toBeInTheDocument();
  });

  it("las opciones están en español, sin códigos", () => {
    pintar();
    expect(opciones("Estado del vehículo")).toEqual(["Todos", "En proceso", "Terminado"]);
    expect(opciones("Dictamen")).toEqual(["Todos", "Viable", "Bloqueado", "Sin dictamen"]);
    expect(opciones("Saneamiento")).toEqual([
      "Todos",
      "Con saneamiento",
      "Sin saneamiento",
      "Saneamiento solicitado",
      "En saneamiento",
      "Saneado",
      "Saneamiento no procede",
    ]);
    expect(opciones("Modo")).toEqual(["Todos", "Simple", "Con análisis IA"]);
    expect(opciones("Trámite")).toEqual(["Todos", "Con trámite", "Sin trámite"]);
  });
});

describe("FiltrosVehiculos — AC21: cada control emite su cambio", () => {
  it("Placa: se ve en mayúsculas y aplica 400 ms después de dejar de escribir", () => {
    const { onCambiar } = pintar();
    const placa = screen.getByRole("textbox", { name: "Placa" });
    fireEvent.change(placa, { target: { value: "ab" } });
    fireEvent.change(placa, { target: { value: "abc" } });
    expect(placa).toHaveValue("ABC");
    act(() => vi.advanceTimersByTime(ESPERA_PLACA_MS - 1));
    expect(onCambiar).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(1));
    expect(onCambiar).toHaveBeenCalledTimes(1);
    expect(onCambiar).toHaveBeenCalledWith({ placa: "ABC" });
    expect(ESPERA_PLACA_MS).toBe(400);
  });

  it("Placa con Enter aplica de inmediato y no vuelve a aplicar al vencer la espera", () => {
    const { onCambiar } = pintar();
    const placa = screen.getByRole("textbox", { name: "Placa" });
    fireEvent.change(placa, { target: { value: "tst1" } });
    fireEvent.keyDown(placa, { key: "Enter" });
    expect(onCambiar).toHaveBeenCalledWith({ placa: "TST1" });
    act(() => vi.advanceTimersByTime(ESPERA_PLACA_MS));
    expect(onCambiar).toHaveBeenCalledTimes(1);
  });

  it("la placa sigue a los filtros cuando cambian desde fuera (Limpiar filtros, volver atrás)", () => {
    const { rerender, onCambiar, onLimpiar, onActualizar } = pintar({ valor: vacios({ placa: "TST" }) });
    expect(screen.getByRole("textbox", { name: "Placa" })).toHaveValue("TST");
    rerender(<FiltrosVehiculos valor={vacios()} error={null} onCambiar={onCambiar} onLimpiar={onLimpiar} onActualizar={onActualizar} />);
    expect(screen.getByRole("textbox", { name: "Placa" })).toHaveValue("");
  });

  it.each([
    ["Estado del vehículo", "TERMINADO", { estado: "TERMINADO" }],
    ["Estado del vehículo", "", { estado: null }],
    ["Dictamen", "SIN_DICTAMEN", { veredicto: "SIN_DICTAMEN" }],
    ["Saneamiento", "CON", { saneamiento: "CON", saneamientoEstado: null }],
    ["Saneamiento", "SIN", { saneamiento: "SIN", saneamientoEstado: null }],
    ["Saneamiento", "SOLICITADO", { saneamiento: "CON", saneamientoEstado: "SOLICITADO" }],
    ["Saneamiento", "NO_PROCEDE", { saneamiento: "CON", saneamientoEstado: "NO_PROCEDE" }],
    ["Saneamiento", "", { saneamiento: null, saneamientoEstado: null }],
    ["Modo", "CON_ANALISIS", { modo: "CON_ANALISIS" }],
    ["Trámite", "true", { conTramite: true }],
    ["Trámite", "false", { conTramite: false }],
    ["Trámite", "", { conTramite: null }],
  ])("«%s» = %s emite %o", (nombre, valor, esperado) => {
    const { onCambiar } = pintar();
    fireEvent.change(screen.getByRole("combobox", { name: nombre }), { target: { value: valor } });
    expect(onCambiar).toHaveBeenCalledWith(esperado);
  });

  it("los selects muestran el filtro vigente: un estado de saneamiento manda sobre «Con saneamiento»", () => {
    pintar({ valor: vacios({ saneamiento: "CON", saneamientoEstado: "EN_GESTION", conTramite: false, veredicto: "VIABLE" }) });
    expect(screen.getByRole("combobox", { name: "Saneamiento" })).toHaveValue("EN_GESTION");
    expect(screen.getByRole("combobox", { name: "Trámite" })).toHaveValue("false");
    expect(screen.getByRole("combobox", { name: "Dictamen" })).toHaveValue("VIABLE");
  });

  it("Desde y Hasta emiten la fecha y se acotan entre sí", () => {
    const { onCambiar } = pintar({ valor: vacios({ desde: "2026-11-01", hasta: "2026-11-30" }) });
    expect(screen.getByLabelText("Desde")).toHaveAttribute("max", "2026-11-30");
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("min", "2026-11-01");
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-11-05" } });
    expect(onCambiar).toHaveBeenCalledWith({ desde: "2026-11-05" });
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2026-11-20" } });
    expect(onCambiar).toHaveBeenCalledWith({ hasta: "2026-11-20" });
  });

  it("«Limpiar filtros» y «Actualizar» llaman a su acción", () => {
    const { onLimpiar, onActualizar } = pintar();
    fireEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    fireEvent.click(screen.getByRole("button", { name: "Actualizar" }));
    expect(onLimpiar).toHaveBeenCalledTimes(1);
    expect(onActualizar).toHaveBeenCalledTimes(1);
  });
});

describe("FiltrosVehiculos — AC21/AC41: error del 400 junto a su control", () => {
  it("un 400 con campo deja el control con aria-invalid y el mensaje como descripción", () => {
    pintar({ error: { campo: "hasta", mensaje: "La fecha final no puede ser anterior a la inicial." } });
    const hasta = screen.getByLabelText("Hasta");
    expect(hasta).toHaveAttribute("aria-invalid", "true");
    expect(hasta).toHaveAccessibleDescription("La fecha final no puede ser anterior a la inicial.");
    expect(screen.getByLabelText("Desde")).not.toHaveAttribute("aria-invalid");
  });

  it("saneamiento_estado marca el select «Saneamiento»", () => {
    pintar({ error: { campo: "saneamiento_estado", mensaje: "Elige una opción de saneamiento de la lista." } });
    expect(screen.getByRole("combobox", { name: "Saneamiento" })).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("combobox", { name: "Saneamiento" })).toHaveAccessibleDescription("Elige una opción de saneamiento de la lista.");
  });

  it("la placa inválida (400 en placa) también se marca", () => {
    pintar({ error: { campo: "placa", mensaje: "Revisa la placa." } });
    expect(screen.getByRole("textbox", { name: "Placa" })).toHaveAccessibleDescription("Revisa la placa.");
  });
});

describe("FiltrosVehiculos — AC42: 360 px y tokens", () => {
  it("los controles envuelven en una columna a 360 px y el archivo no tiene colores sueltos", () => {
    pintar();
    expect(screen.getByRole("group", { name: "Filtros de vehículos" }).className).toMatch(/flex-wrap/);
    const fuente = readFileSync(join(__dirname, "..", "FiltrosVehiculos.tsx"), "utf8");
    expect(fuente).not.toMatch(/#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/);
  });
});

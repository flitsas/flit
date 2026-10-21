import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ParametrizacionCompacta } from "@/components/parametrizacion/ParametrizacionCompacta";
import { configuracion, simularApi, tiposOk } from "./datos";

// Uso de ejemplo: <ParametrizacionCompacta identificador="ABC123" configuracion={config} onContinuar={(cabecera) => ...} />
// propone «Express ABC123 21/10/2026» como nombre y entrega la cabecera al continuar.

afterEach(() => vi.unstubAllGlobals());

const FECHA = new Date("2026-10-21T15:30:00Z");

function montar() {
  simularApi({ "GET /tipos-tramite": tiposOk });
  const onContinuar = vi.fn();
  render(<ParametrizacionCompacta identificador="ABC123" fecha={FECHA} configuracion={configuracion()} onContinuar={onContinuar} />);
  return onContinuar;
}

const nombre = () => screen.getByRole("textbox", { name: "Nombre del diagnóstico" });

describe("ParametrizacionCompacta — nombre del Express (AC 6)", () => {
  it("abre con el nombre autogenerado con el identificador y la fecha", () => {
    montar();
    expect(nombre()).toHaveValue("Express ABC123 21/10/2026");
  });

  it("el nombre es editable y se entrega al continuar con el alcance y el modo", () => {
    const onContinuar = montar();
    fireEvent.change(nombre(), { target: { value: "Revisión ABC123" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    expect(onContinuar).toHaveBeenCalledWith({
      nombre: "Revisión ABC123",
      modo: "SIMPLE",
      tipo_tramite_codigo: null,
      alcance: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] },
    });
  });

  it("con el nombre vacío no deja continuar y muestra el error en el campo", () => {
    const onContinuar = montar();
    fireEvent.change(nombre(), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));
    expect(onContinuar).not.toHaveBeenCalled();
    expect(nombre()).toHaveAttribute("aria-invalid", "true");
    expect(nombre()).toHaveAccessibleDescription(/escribe el nombre del diagnóstico/i);
  });

  it("trae el alcance y el modo en versión compacta", () => {
    montar();
    expect(screen.getByRole("checkbox", { name: "RUNT vehículo" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Simple" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Con Análisis IA" })).not.toBeChecked();
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { FiltrosReporte } from "@/components/reportes/FiltrosReporte";
import { filtrosPorDefecto } from "@/lib/reportes/catalogo";
import { CONSULTAS, VEHICULOS } from "@/lib/reportes/__tests__/datosReportes";
import { simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <FiltrosReporte reporte={catalogo.reportes[0]} valor={filtros} hoy="2026-11-15" vistaGlobal={false}
// onCambiar={setFiltros} /> pinta el periodo, la compañía (solo vista global), los enums del catálogo como chips y la
// placa (prefijo, nunca en la URL). Cada cambio vuelve a la página 1 en el módulo.

const HOY = "2026-11-15";
beforeEach(() => simularApi({ "GET /backoffice/empresas": () => Response.json([]) }));
afterEach(() => vi.unstubAllGlobals());

describe("FiltrosReporte — filtros del catálogo", () => {
  it("pinta los enums del reporte como chips con nombre de negocio; uno no repetible reemplaza el valor", () => {
    const onCambiar = vi.fn();
    render(<FiltrosReporte reporte={VEHICULOS} valor={filtrosPorDefecto(HOY)} hoy={HOY} vistaGlobal={false} onCambiar={onCambiar} />);
    const modalidad = screen.getByRole("group", { name: "Modalidad" });
    expect(within(modalidad).getAllByRole("button").map((b) => b.textContent)).toEqual(["Express", "Masivo"]);
    fireEvent.click(within(modalidad).getByRole("button", { name: "Masivo" }));
    expect(onCambiar).toHaveBeenLastCalledWith(expect.objectContaining({ filtros: { modalidad: ["MASIVO"] } }));
  });

  it("un enum repetible acumula valores y el activo se marca con aria-pressed", () => {
    const onCambiar = vi.fn();
    const valor = { ...filtrosPorDefecto(HOY), filtros: { estado: ["ERROR"] } };
    render(<FiltrosReporte reporte={CONSULTAS} valor={valor} hoy={HOY} vistaGlobal={false} onCambiar={onCambiar} />);
    const estado = screen.getByRole("group", { name: "Estado" });
    expect(within(estado).getByRole("button", { name: "Con error" })).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(within(estado).getByRole("button", { name: "No aplica" }));
    expect(onCambiar).toHaveBeenLastCalledWith(expect.objectContaining({ filtros: { estado: ["ERROR", "NA"] } }));
  });

  it("la placa se aplica al confirmar (Enter) y no al teclear", () => {
    const onCambiar = vi.fn();
    render(<FiltrosReporte reporte={VEHICULOS} valor={filtrosPorDefecto(HOY)} hoy={HOY} vistaGlobal={false} onCambiar={onCambiar} />);
    const placa = screen.getByLabelText("Placa (inicio)");
    fireEvent.change(placa, { target: { value: "abc" } });
    expect(onCambiar).not.toHaveBeenCalled();
    fireEvent.submit(placa);
    expect(onCambiar).toHaveBeenLastCalledWith(expect.objectContaining({ placa: "ABC" }));
  });

  it("«Limpiar filtros» solo aparece con algún filtro distinto del predeterminado y vuelve a los valores por defecto", () => {
    const onCambiar = vi.fn();
    const { rerender } = render(<FiltrosReporte reporte={VEHICULOS} valor={filtrosPorDefecto(HOY)} hoy={HOY} vistaGlobal={false} onCambiar={onCambiar} />);
    expect(screen.queryByRole("button", { name: "Limpiar filtros" })).not.toBeInTheDocument();
    rerender(<FiltrosReporte reporte={VEHICULOS} valor={{ ...filtrosPorDefecto(HOY), placa: "ABC" }} hoy={HOY} vistaGlobal={false} onCambiar={onCambiar} />);
    fireEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(onCambiar).toHaveBeenLastCalledWith(filtrosPorDefecto(HOY));
  });

  it("el selector de compañía solo está en la vista global", () => {
    const { rerender } = render(<FiltrosReporte reporte={VEHICULOS} valor={filtrosPorDefecto(HOY)} hoy={HOY} vistaGlobal={false} onCambiar={() => {}} />);
    expect(screen.queryByRole("combobox", { name: "Compañía" })).not.toBeInTheDocument();
    rerender(<FiltrosReporte reporte={VEHICULOS} valor={filtrosPorDefecto(HOY)} hoy={HOY} vistaGlobal onCambiar={() => {}} />);
    expect(screen.getByRole("combobox", { name: "Compañía" })).toBeInTheDocument();
  });
});

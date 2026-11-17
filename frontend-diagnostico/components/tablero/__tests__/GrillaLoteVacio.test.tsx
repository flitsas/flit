import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { GrillaLote, type GrillaLoteProps } from "@/components/tablero/GrillaLote";
import { REGION_VIVA_RETARDO_MS } from "@flit/ui/useRegionViva";
import { grilla } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <GrillaLote grilla={grilla} filtro={filtro} onQuitarFiltro={() => setFiltro(null)} … /> en el tablero.
// HU #14562 · m-12 (auditoría de UI): si la tarjeta-filtro o la búsqueda dejan la grilla sin filas, se anuncia el vacío
// «Ningún vehículo coincide» con «Quitar filtro», en vez de una tabla sin filas.

const pintar = (props: Partial<GrillaLoteProps> = {}) =>
  render(
    <GrillaLote
      grilla={grilla({ estado: "COMPLETADO" })}
      filtro={null}
      puedeReprocesar={false}
      onReprocesada={vi.fn()}
      onErrorReproceso={vi.fn()}
      tituloAccionVehiculo="Reporte"
      onAbrirDictamen={vi.fn()}
      {...props}
    />,
  );

const esperarRegion = () =>
  act(async () => {
    await new Promise((r) => setTimeout(r, REGION_VIVA_RETARDO_MS + 10));
  });

describe("GrillaLote — HU #14562 m-12: vacío por filtro", () => {
  it("una búsqueda sin resultados muestra «Ningún vehículo coincide» y «Quitar filtro» limpia la búsqueda", async () => {
    pintar();
    fireEvent.change(screen.getByLabelText("Buscar por placa o VIN"), { target: { value: "ZZZ999" } });
    await esperarRegion();
    expect(screen.getByRole("status")).toHaveTextContent("Ningún vehículo coincide");
    expect(screen.queryByRole("table", { name: /semáforo de consultas/i })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Quitar filtro" }));
    expect(screen.getByLabelText("Buscar por placa o VIN")).toHaveValue("");
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
  });

  it("una tarjeta-filtro sin vehículos muestra el vacío y «Quitar filtro» avisa a quien la controla", async () => {
    const onQuitarFiltro = vi.fn();
    pintar({ filtro: "con_deuda", grilla: grilla({ estado: "COMPLETADO", vehiculos: [] }), onQuitarFiltro });
    await esperarRegion();
    expect(screen.getByRole("status")).toHaveTextContent("Ningún vehículo coincide");
    fireEvent.click(screen.getByRole("button", { name: "Quitar filtro" }));
    expect(onQuitarFiltro).toHaveBeenCalledTimes(1);
  });

  it("sin filtro ni búsqueda la grilla se pinta normal, sin el vacío", () => {
    pintar();
    expect(screen.getByRole("table", { name: /semáforo de consultas/i })).toBeInTheDocument();
    expect(screen.queryByText("Ningún vehículo coincide")).toBeNull();
  });
});

import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { TablaReporte } from "@/components/reportes/TablaReporte";
import { paginaVehiculos } from "@/lib/reportes/__tests__/datosReportes";

// Uso de ejemplo: <TablaReporte tipo="vehiculos" pagina={pagina} cargando={false} tamano={25} onPagina={…} onTamano={…} />
// pinta las columnas y filas que manda la API (claves = columnas[].clave) con el formato por tipo, y la paginación del
// kit con 25/50/100 filas por página (Q3) y el tope de desplazamiento de 10 000 registros.

describe("TablaReporte — tabla genérica por columnas del contrato", () => {
  it("pinta los encabezados en el orden de columnas y las celdas con su formato", () => {
    render(<TablaReporte tipo="vehiculos" pagina={paginaVehiculos()} cargando={false} tamano={25} onPagina={() => {}} onTamano={() => {}} />);
    const tabla = screen.getByRole("table", { name: "Vehículos diagnosticados" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual([
      "Fecha del diagnóstico",
      "Diagnóstico",
      "Modalidad",
      "Placa",
      "Propietarios (documento)",
      "Veredicto",
    ]);
    const fila = within(tabla).getAllByRole("row")[1];
    expect(within(fila).getByText("22/10/2026 09:00")).toBeInTheDocument();
    expect(within(fila).getByText("Express")).toBeInTheDocument();
    expect(within(fila).getByText("CC 0000000")).toHaveAttribute("title", "CC 0000000");
    expect(within(fila).getByText("—")).toBeInTheDocument();
  });

  it("la paginación ofrece 25, 50 y 100 filas por página y avisa el cambio de tamaño", () => {
    const onTamano = vi.fn();
    render(<TablaReporte tipo="vehiculos" pagina={paginaVehiculos()} cargando={false} tamano={25} onPagina={() => {}} onTamano={onTamano} />);
    const selector = screen.getByRole("combobox", { name: "Filas por página" });
    expect(within(selector).getAllByRole("option").map((o) => o.textContent)).toEqual(["25", "50", "100"]);
    fireEvent.change(selector, { target: { value: "50" } });
    expect(onTamano).toHaveBeenCalledWith(50);
    expect(screen.getByRole("navigation", { name: /Paginación/ })).toBeInTheDocument();
  });

  it("con más de 10 000 registros la paginación dice el total real, avisa el tope y no pasa de él (UA-5)", () => {
    const onPagina = vi.fn();
    render(<TablaReporte tipo="vehiculos" pagina={paginaVehiculos({ total: 12000 })} cargando={false} tamano={100} onPagina={onPagina} onTamano={() => {}} />);
    expect(screen.getByText(/de 12000/)).toBeInTheDocument();
    expect(screen.queryByText(/de 10000/)).toBeNull();
    expect(screen.getByText("Se muestran los primeros 10.000 de 12.000 registros. Acota el filtro para ver más.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Última página" }));
    expect(onPagina).toHaveBeenCalledWith(100);
  });

  it("cargando: esqueleto accesible sin filas", () => {
    render(<TablaReporte tipo="vehiculos" pagina={null} cargando tamano={25} onPagina={() => {}} onTamano={() => {}} />);
    expect(screen.getAllByRole("status").length).toBeGreaterThan(0);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});

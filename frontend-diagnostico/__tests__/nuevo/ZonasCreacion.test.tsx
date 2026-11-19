import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ZonasCreacion } from "@/components/nuevo/ZonasCreacion";

// Uso de ejemplo: <ZonasCreacion parametros={<form>…</form>} vehiculos={<CamposExpress />} ayudaVehiculos="…"
//   despuesVehiculos={<ResumenValidacion />} pie={<BarraAccion />} rejillaRef={ref} />
// Ola Diagnóstico UX 2026-11-15 · FE-3 (spec-ui §3): una sola columna de arriba abajo — Parámetros (sin sticky), Vehículos,
// lo que sigue a Vehículos (resumen y filas) y al final la barra con el estimado y la acción.

const pintar = () =>
  render(
    <ZonasCreacion
      parametros={<input aria-label="Nombre del diagnóstico" />}
      vehiculos={<input aria-label="Placa o VIN" />}
      ayudaVehiculos="Escribe la placa."
      despuesVehiculos={<p>Resumen de la carga</p>}
      pie={<button type="submit">Continuar</button>}
    />,
  );

const sigue = (a: Element, b: Element) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;

describe("ZonasCreacion (FE-3)", () => {
  it("orden DOM = orden visual: Parámetros, Vehículos, el resumen y al final la acción", () => {
    pintar();
    const orden = [
      screen.getByRole("region", { name: "Parámetros" }),
      screen.getByRole("region", { name: "Vehículos" }),
      screen.getByText("Resumen de la carga"),
      screen.getByRole("button", { name: "Continuar" }),
    ];
    for (let i = 1; i < orden.length; i++) expect(sigue(orden[i - 1], orden[i])).toBe(true);
    expect(screen.getByRole("textbox", { name: "Nombre del diagnóstico" })).toBeInTheDocument();
    expect(screen.getByText("Escribe la placa.")).toBeInTheDocument();
  });

  it("una sola columna: sin rejilla de dos columnas y Parámetros ya no es sticky", () => {
    pintar();
    const parametros = screen.getByRole("region", { name: "Parámetros" });
    const contenedor = parametros.parentElement as HTMLElement;
    expect(contenedor.className).toMatch(/(^|\s)flex-col(\s|$)/);
    expect(contenedor.className).not.toMatch(/grid-cols-/);
    expect(parametros.className).not.toMatch(/sticky|max-h-|overflow-y-auto/);
    expect(parametros.className).toContain("min-w-0");
    expect(screen.getByRole("region", { name: "Vehículos" }).className).toContain("min-w-0");
  });

  it("la barra de acción es una tarjeta propia, al final, que apila en móvil y alinea en fila desde sm", () => {
    pintar();
    const barra = screen.getByRole("button", { name: "Continuar" }).closest("[data-parte='pie-creacion']") as HTMLElement;
    expect(barra).not.toBeNull();
    expect(barra.className).toContain("flex-col");
    expect(barra.className).toContain("sm:flex-row");
    expect(barra.parentElement?.lastElementChild).toBe(barra);
  });

  it("sin pie no pinta la barra", () => {
    render(<ZonasCreacion parametros={<p>p</p>} vehiculos={<p>v</p>} />);
    expect(document.querySelector("[data-parte='pie-creacion']")).toBeNull();
  });
});

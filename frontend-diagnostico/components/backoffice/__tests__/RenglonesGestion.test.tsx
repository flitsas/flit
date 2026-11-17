import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RenglonesLectura } from "@/components/backoffice/RenglonesGestion";
import { renglon, renglonSoat } from "@/components/saneamiento/__tests__/datosBackoffice";
import { esperarCabecera12px, esperarTablaPorDefecto } from "@/components/comun/__tests__/tablaPorDefecto";

// Uso de ejemplo: <RenglonesLectura renglones={saneamiento.renglones} verEvidencia={abrir} descargas={{}} /> en la gestión de un
// saneamiento cerrado. HU #14585 (spec-ui §7, A7.1): «Conceptos a sanear» con la tabla por defecto (contenedor, cabecera de
// 12 px y hover de fila), sin perder sus tarjetas por debajo de 900 px.

describe("RenglonesGestion — HU #14585: tabla por defecto", () => {
  it("conceptos de solo lectura con contenedor, cabecera y hover de la tabla por defecto", () => {
    render(<RenglonesLectura renglones={[renglon(), renglonSoat()]} verEvidencia={vi.fn()} descargas={{}} />);
    esperarTablaPorDefecto(screen.getByRole("table", { name: "Conceptos a sanear" }));
    esperarCabecera12px();
  });

  it("sin conceptos: el vacío de siempre, sin tabla", () => {
    render(<RenglonesLectura renglones={[]} verEvidencia={vi.fn()} descargas={{}} />);
    expect(screen.getByText("El saneamiento no tiene conceptos.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).toBeNull();
  });
});

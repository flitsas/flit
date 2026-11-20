import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { FiltrosListado } from "@/components/listado/FiltrosListado";

// Uso de ejemplo: <FiltrosListado valor={filtros} error={{ campo: "modo", mensaje: "El tipo no es válido." }}
//   onCambiar={aplicar} onLimpiar={limpiar} />
// HU #14562 · M10 (auditoría): el selector «Tipo» toma el nombre de su rótulo visible (etiquetadoPor) y, con un 400 sobre
// `modo`, el mensaje queda asociado al radiogroup por describedBy, como en los demás controles.

const vacio = { desde: "", hasta: "", modo: null, estado: null, nombre: "", pagina: 1, tamano: 20 } as never;

describe("FiltrosListado: selector Tipo (HU #14562 · M10)", () => {
  it("M10: el radiogroup «Tipo» se nombra con el rótulo visible, sin aria-label duplicado", () => {
    render(<FiltrosListado valor={vacio} error={null} onCambiar={vi.fn()} />);
    const grupo = screen.getByRole("radiogroup", { name: "Tipo" });
    expect(grupo).toHaveAttribute("aria-labelledby");
    expect(grupo).not.toHaveAttribute("aria-label");
  });

  it("M10: el error del 400 sobre «modo» es la descripción accesible del radiogroup", () => {
    render(<FiltrosListado valor={vacio} error={{ campo: "modo", mensaje: "El tipo pedido no existe." }} onCambiar={vi.fn()} />);
    expect(screen.getByRole("radiogroup", { name: "Tipo" })).toHaveAccessibleDescription("El tipo pedido no existe.");
  });

  it("M10: sin error, el radiogroup no tiene descripción", () => {
    render(<FiltrosListado valor={vacio} error={null} onCambiar={vi.fn()} />);
    expect(screen.getByRole("radiogroup", { name: "Tipo" })).not.toHaveAttribute("aria-describedby");
  });
});

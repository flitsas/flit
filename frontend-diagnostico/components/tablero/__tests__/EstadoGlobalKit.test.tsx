import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { EstadoGlobal, EstimadoVsConsumido } from "@/components/tablero/EstadoGlobal";
import { grilla } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <EstadoGlobal grilla={grilla} sondeando /> en el tablero. HU #14562, auditoría de UI m-5: el avance usa
// BarraProgreso del kit (que queda quieta con menos movimiento) y el lote EN_PROCESO, la EtiquetaEnProceso con su punto
// pulsante; m-4: la tabla del estimado lleva la cabecera del kit (13 px en negrita).

describe("EstadoGlobal — HU #14562 m-5: componentes del kit en el tablero", () => {
  it("el avance es la BarraProgreso del kit, con su relleno sin animación cuando se pide menos movimiento", () => {
    render(<EstadoGlobal grilla={grilla({ avance: { vehiculos: 4, terminados: 1 } })} sondeando={false} />);
    const barra = screen.getByRole("progressbar", { name: "Vehículos terminados" });
    expect(barra).toHaveAttribute("aria-valuenow", "1");
    expect(barra).toHaveAttribute("aria-valuemax", "4");
    expect(barra).toHaveAttribute("aria-valuetext", "1 de 4 vehículos terminados");
    const relleno = barra.querySelector("[data-parte='relleno']") as HTMLElement;
    expect(relleno).not.toBeNull();
    expect(relleno.className).toContain("motion-reduce:transition-none");
    expect(relleno).toHaveStyle({ width: "25%" });
  });

  it("el lote EN_PROCESO se ve con la EtiquetaEnProceso (punto pulsante solo con movimiento)", () => {
    const { container } = render(<EstadoGlobal grilla={grilla({ estado: "EN_PROCESO" })} sondeando />);
    const etiqueta = container.querySelector("[data-estado='en-proceso']") as HTMLElement;
    expect(etiqueta).not.toBeNull();
    expect(etiqueta).toHaveTextContent("En proceso");
    expect(etiqueta.querySelector("[data-marca='punto']")!.className).toContain("motion-safe:animate-pulse");
  });

  it("N2: el lote EN_PROCESO se nombra igual que los demás estados, «Estado del lote: En proceso»", () => {
    render(<EstadoGlobal grilla={grilla({ estado: "EN_PROCESO" })} sondeando />);
    expect(screen.getByRole("status", { name: "Estado del lote: En proceso" })).toHaveTextContent("En proceso");
  });

  it("un lote que no está en proceso conserva su chip con «Estado del lote: …» y sin punto pulsante", () => {
    const { container } = render(<EstadoGlobal grilla={grilla({ estado: "COMPLETADO" })} sondeando={false} />);
    expect(screen.getByRole("status", { name: "Estado del lote: Completado" })).toBeInTheDocument();
    expect(container.querySelector("[data-estado='en-proceso']")).toBeNull();
  });
});

describe("EstimadoVsConsumido — HU #14562 m-4: cabecera del kit", () => {
  it("las cabeceras van en 13 px y negrita", () => {
    render(<EstimadoVsConsumido estimado={[{ fuente: "RUNT", estimadas: 2, consumidas: 2, minimo: false }]} />);
    for (const th of screen.getAllByRole("columnheader")) {
      expect(th.className).toContain("text-[0.8125rem]");
      expect(th.className).toContain("font-bold");
    }
  });
});

import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { EstimadoVsConsumido } from "@/components/tablero/EstadoGlobal";

// Uso de ejemplo: <EstimadoVsConsumido estimado={grilla.estimado_consultas} /> pinta, al finalizar el lote, las consultas
// estimadas frente a las consumidas por fuente. Ampliación de HU #13802 (decisión del usuario 2026-10-22): con minimo=true
// (#13796, algún vehículo sin personas registradas) las estimadas se leen «al menos N», con la misma ayuda accesible que
// ListaEstimado (#13797) enlazada por aria-describedby; sin la marca o sin la propiedad, el número tal cual.

const region = () => screen.getByRole("region", { name: /estimado frente a consumido/i });
const celdaEstimadas = (fuente: RegExp) => within(within(region()).getByRole("row", { name: fuente })).getAllByRole("cell")[0];

describe("EstimadoVsConsumido — rótulo «al menos N» (ampliación HU #13802)", () => {
  it("con minimo=true las estimadas dicen «al menos N» y quedan descritas por la ayuda", () => {
    render(
      <EstimadoVsConsumido
        estimado={[
          { fuente: "RUNT", estimadas: 2, consumidas: 2, minimo: false },
          { fuente: "SIMIT_PROPIETARIO", estimadas: 3, consumidas: 4, minimo: true },
        ]}
      />,
    );
    const simit = celdaEstimadas(/SIMIT propietario/);
    expect(simit).toHaveTextContent(/^al menos 3$/);
    const ayudaId = simit.getAttribute("aria-describedby");
    expect(ayudaId).toBeTruthy();
    expect(document.getElementById(ayudaId!)).toHaveTextContent(/mínimo/);
    const runt = celdaEstimadas(/RUNT vehículo/);
    expect(runt).toHaveTextContent(/^2$/);
    expect(runt).not.toHaveAttribute("aria-describedby");
  });

  it("sin la marca (propiedad ausente) el número tal cual y sin ayuda", () => {
    render(<EstimadoVsConsumido estimado={[{ fuente: "SIMIT_PROPIETARIO", estimadas: 3, consumidas: 3 }]} />);
    expect(celdaEstimadas(/SIMIT propietario/)).toHaveTextContent(/^3$/);
    expect(within(region()).queryByText(/al menos/i)).toBeNull();
  });
});

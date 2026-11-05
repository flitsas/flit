import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { EstimadoConsultas } from "@/components/carga/EstimadoConsultas";

// Uso de ejemplo (HU #13797, F05-H8): <EstimadoConsultas estimado={[{ fuente: "SIMIT_PROPIETARIO", consultas: 10, minimo: true }]} />
// pinta «al menos 10» en SIMIT propietario y un texto de ayuda; una fuente sin la marca muestra el número tal cual.
// El Express (POST /express/estimado) usa el mismo rótulo: lo cubre __tests__/nuevo/express.test.tsx (HU #14085).


const AYUDA = /es un mínimo/i;

function valorDe(region: HTMLElement, fuente: string) {
  const termino = within(region).getByText(fuente, { selector: "dt" });
  return termino.nextElementSibling as HTMLElement;
}

describe("EstimadoConsultas — fuentes por persona como mínimo (HU #13797)", () => {
  it("AC1: SIMIT_PROPIETARIO con minimo=true se lee «al menos 10» con su texto de ayuda", () => {
    render(
      <EstimadoConsultas
        estimado={[
          { fuente: "SIMIT_VEHICULO", consultas: 10, minimo: false },
          { fuente: "SIMIT_PROPIETARIO", consultas: 10, minimo: true },
        ]}
      />,
    );
    const region = screen.getByRole("region", { name: "Estimado de consultas" });
    const valor = valorDe(region, "SIMIT propietario");
    expect(valor).toHaveTextContent(/^al menos 10$/);
    const ayuda = within(region).getByText(AYUDA);
    expect(valor).toHaveAttribute("aria-describedby", ayuda.id);
  });

  it("AC2: SIMIT_VEHICULO sin la marca se lee el número sin «al menos» y sin ayuda", () => {
    render(<EstimadoConsultas estimado={[{ fuente: "SIMIT_VEHICULO", consultas: 10, minimo: false }]} />);
    const region = screen.getByRole("region", { name: "Estimado de consultas" });
    const valor = valorDe(region, "SIMIT vehículo");
    expect(valor).toHaveTextContent(/^10$/);
    expect(valor).not.toHaveAttribute("aria-describedby");
    expect(within(region).queryByText(AYUDA)).not.toBeInTheDocument();
  });

  it("AC2 (contrato previo): una respuesta sin la propiedad minimo pinta el número tal cual", () => {
    render(<EstimadoConsultas estimado={[{ fuente: "RUNT", consultas: 3 }]} />);
    expect(valorDe(screen.getByRole("region", { name: "Estimado de consultas" }), "RUNT vehículo")).toHaveTextContent(/^3$/);
  });
});

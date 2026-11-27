import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MontoTotal } from "@/components/estadisticas/MontoTotal";

// Uso de ejemplo: <MontoTotal montoTotal={1234567} liquidacionesSinMonto={3} parcial={false} /> en el dashboard. El monto
// es la suma de totalAPagar sin los null (impuestos.v1.yaml, Dashboard.montoTotal): con N > 0 liquidaciones sin monto se
// muestra x-flit-literales.pantalla.liquidacionesSinMonto y la marca «Parcial»; un null nunca suma como 0.

const tarjeta = () => screen.getByRole("region", { name: "Monto total liquidado" });

describe("MontoTotal (F4a-CF3, total parcial)", () => {
  it("pinta el título y el monto en pesos es-CO, sin marca cuando todo tiene monto", () => {
    render(<MontoTotal montoTotal={1234567} liquidacionesSinMonto={0} parcial={false} />);
    expect(within(tarjeta()).getByRole("heading", { level: 2, name: "Monto total liquidado" })).toBeInTheDocument();
    expect(within(tarjeta()).getByText(/\$\s?1\.234\.567/)).toBeInTheDocument();
    expect(within(tarjeta()).queryByText("Parcial")).not.toBeInTheDocument();
    expect(within(tarjeta()).queryByText(/sin monto/)).not.toBeInTheDocument();
  });

  it("con liquidaciones sin monto: el literal del contrato con {n} y la marca «Parcial»", () => {
    render(<MontoTotal montoTotal={500000} liquidacionesSinMonto={3} parcial={false} />);
    expect(within(tarjeta()).getByText("3 liquidaciones sin monto")).toBeInTheDocument();
    expect(within(tarjeta()).getByText("Parcial")).toBeInTheDocument();
    expect(within(tarjeta()).getByText(/\$\s?500\.000/)).toBeInTheDocument();
  });

  it("con parcial=true y ningún null: marca «Parcial» y la nota de resultados parciales", () => {
    render(<MontoTotal montoTotal={800} liquidacionesSinMonto={0} parcial />);
    expect(within(tarjeta()).getByText("Parcial")).toBeInTheDocument();
    expect(within(tarjeta()).getByText("Incluye resultados parciales del proveedor")).toBeInTheDocument();
  });

  it("un monto 0 es un dato real: «$ 0» y sin «Sin dato»", () => {
    render(<MontoTotal montoTotal={0} liquidacionesSinMonto={0} parcial={false} />);
    expect(within(tarjeta()).getByText(/\$\s?0$/)).toBeInTheDocument();
    expect(within(tarjeta()).queryByText("Sin dato")).not.toBeInTheDocument();
  });

  it("un monto ausente dice «Sin dato» y nunca $ 0", () => {
    render(<MontoTotal montoTotal={null} liquidacionesSinMonto={2} parcial={false} />);
    expect(within(tarjeta()).getByText("Sin dato")).toBeInTheDocument();
    expect(within(tarjeta()).queryByText(/\$\s?0/)).not.toBeInTheDocument();
    expect(within(tarjeta()).getByText("2 liquidaciones sin monto")).toBeInTheDocument();
  });
});

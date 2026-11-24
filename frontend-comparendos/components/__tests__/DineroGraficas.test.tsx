import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { DineroGraficas } from "@/components/reportes/DineroGraficas";
import type { Dinero } from "@/lib/reportes";

// R2: la tendencia, la dona, el dinero por organismo y el cobro coactivo salieron del Inicio y viven en Reportes.
vi.mock("recharts", async (original) => {
  const r = await original<typeof import("recharts")>();
  return { ...r, ResponsiveContainer: () => null };
});

const m = (cantidad: number, valor: number) => ({ cantidad, valor });
const d: Dinero = {
  hoy: "2026-11-19",
  estados: [
    { estado: "SinNotificar", ...m(0, 0) },
    { estado: "EnTiempo", ...m(30, 126_200_000) },
    { estado: "Vencido", ...m(10, 3_800_000) },
    { estado: "Caducado", ...m(2, 900_000) },
    { estado: "Prescrito", ...m(0, 0) },
  ],
  exigible: m(40, 130_000_000),
  noExigible: m(2, 900_000),
  cobroCoactivo: m(3, 2_500_000),
  descuento: { d50: { ...m(0, 0), ahorroEstimado: 0 }, d25: { ...m(0, 0), ahorroEstimado: 0 } },
  organismos: [{ organismoId: "o1", nombre: "Secretaría de Movilidad de Bogotá", cantidad: 42, valor: 130_900_000, exigible: 130_000_000 }],
  pagado: { cantidad: 0, total: 0, cc: 0, pc: 0, bc: 0 },
  frentes: { proximosAVencer: m(0, 0), sinContraventor: m(0, 0), rechazadosOt: m(0, 0), sinGestion: m(0, 0), vencidos: m(0, 0), caducidadInmediata: m(0, 0) },
  ahorroLogrado: 0,
  mensual: [{ periodo: "2026-11", valorIngresado: 1000, pagado: 0 }],
};

describe("DineroGraficas (Reportes)", () => {
  afterEach(cleanup);

  it("el cobro coactivo lleva a la bandeja y se rotula como valor; el reparto por organismo separa lo cobrable", () => {
    render(<DineroGraficas d={d} empresa="e1" />);
    const coactivo = screen.getByRole("link", { name: /^En cobro coactivo: 3 comparendos por un valor de \$\s2\.500\.000/ });
    expect(coactivo.getAttribute("href")).toBe("/bandeja?cobroCoactivo=true&empresa=e1");
    expect(screen.getByText(/^Secretaría de Movilidad de Bogotá: Se puede cobrar \$\s130\.000\.000, Caducado o prescrito \$\s900\.000$/)).toBeTruthy();
    expect(screen.getByText("Tendencia de los últimos 12 meses")).toBeTruthy();
  });

  it("sin cobro coactivo no pinta la tarjeta", () => {
    render(<DineroGraficas d={{ ...d, cobroCoactivo: m(0, 0) }} empresa={null} />);
    expect(screen.queryByRole("link", { name: /^En cobro coactivo/ })).toBeNull();
  });
});

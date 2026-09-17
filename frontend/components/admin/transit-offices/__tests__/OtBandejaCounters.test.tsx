// Feature #12565 — tarjeta "Solicitudes de revocatoria" en la tira de contadores del OT: cuenta
// Aprobados con una solicitud de revocatoria ACTIVA y, al pulsarla, filtra el listado por ese mismo
// criterio. Tras ADR-0059 (Feature #12595) las demás tarjetas SON un estado del ciclo de vida y son
// excluyentes entre sí; esta es la única excepción, y por eso filtra por su propio eje y no por
// `status`. Esa convivencia es justo lo que estas pruebas fijan.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  OtBandejaCountersStrip,
  contadorDeEstado,
  estadoDeContador,
  revocatoriaActivaDeContador,
  type OtCounterKey,
} from "@/components/admin/transit-offices/OtBandejaCounters";
import type { OtBandejaCounters as Counters } from "@/lib/api/types-ot";

const COUNTERS: Counters = {
  transitOfficeResolved: true,
  preasignacion: 4,
  asignados: 2,
  porDecidir: 7,
  aprobados: 5,
  rechazados: 1,
  revocados: 2,
  solicitudesRevocatoria: 3,
};

/** Las seis que ADR-0059 define como estado; la de revocatoria queda fuera a propósito. */
const TARJETAS_DE_ESTADO: OtCounterKey[] = [
  "preasignacion",
  "asignados",
  "porDecidir",
  "aprobados",
  "rechazados",
  "revocados",
];

describe("contadores del OT — la tarjeta de revocatoria filtra por su propio eje", () => {
  it("no filtra por status: el sub-flujo es ortogonal al estado (ADR-0022)", () => {
    expect(estadoDeContador("solicitudesRevocatoria")).toBe("");
    expect(revocatoriaActivaDeContador("solicitudesRevocatoria")).toBe(true);
  });

  it("las tarjetas de estado filtran por status y no encienden el flag de revocatoria", () => {
    for (const key of TARJETAS_DE_ESTADO) {
      expect(estadoDeContador(key)).not.toBe("");
      expect(revocatoriaActivaDeContador(key)).toBeUndefined();
    }
  });

  it("un filtro de estado vacío no marca la tarjeta de revocatoria como activa", () => {
    // Su `status` es "": sin el guard, empataría con «sin filtro» y se encendería sola.
    expect(contadorDeEstado("")).toBe("");
    expect(contadorDeEstado("aprobado")).toBe("aprobados");
    expect(contadorDeEstado("revocado")).toBe("revocados");
  });
});

describe("OtBandejaCountersStrip — Solicitudes de revocatoria", () => {
  it("muestra la cifra del backend y notifica la selección al pulsarla", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<OtBandejaCountersStrip counters={COUNTERS} selected="" onSelect={onSelect} />);

    const tarjeta = screen.getByRole("button", { name: /Solicitudes de revocatoria: 3/i });
    expect(tarjeta).toBeInTheDocument();

    await user.click(tarjeta);
    expect(onSelect).toHaveBeenCalledWith("solicitudesRevocatoria");
  });

  it("pulsarla de nuevo estando activa la apaga (toggle)", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(
      <OtBandejaCountersStrip
        counters={COUNTERS}
        selected="solicitudesRevocatoria"
        onSelect={onSelect}
      />,
    );

    await user.click(screen.getByRole("button", { name: /Solicitudes de revocatoria: 3/i }));
    expect(onSelect).toHaveBeenCalledWith("");
  });

  it("convive con las seis tarjetas de estado de ADR-0059", () => {
    render(<OtBandejaCountersStrip counters={COUNTERS} selected="" onSelect={vi.fn()} />);

    expect(screen.getAllByRole("button")).toHaveLength(TARJETAS_DE_ESTADO.length + 1);
    expect(screen.getByRole("button", { name: /Preasignación: 4/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Revocados: 2/i })).toBeInTheDocument();
  });
});

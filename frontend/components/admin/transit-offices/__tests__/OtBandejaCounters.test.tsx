// Pedido del usuario (2026-09-16) — tarjeta "Solicitudes de revocatoria" en la tira de contadores
// del OT: cuenta Aprobados con una solicitud de revocatoria ACTIVA y, al pulsarla, filtra el
// listado por ese mismo criterio (no por `status`, que la tarjeta deja vacío a propósito).
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  OtBandejaCountersStrip,
  filtrosDeContador,
  type OtCounterKey,
} from "@/components/admin/transit-offices/OtBandejaCounters";
import type { OtBandejaCounters as Counters } from "@/lib/api/types-ot";

const COUNTERS: Counters = {
  transitOfficeResolved: true,
  sinAsignarPlaca: 0,
  conPlacaAsignada: 0,
  aprobados: 5,
  rechazados: 1,
  sinGestion: 0,
  revocados: 2,
  solicitudesRevocatoria: 3,
};

describe("filtrosDeContador — Solicitudes de revocatoria", () => {
  it("filtra por revocatoria activa, sin restringir por status", () => {
    expect(filtrosDeContador("solicitudesRevocatoria")).toEqual({
      status: "",
      plateFlowStatus: "",
      hasActiveRevocationRequest: true,
    });
  });

  it("las demás tarjetas no encienden el filtro de revocatoria", () => {
    const otras: OtCounterKey[] = [
      "sinAsignarPlaca",
      "conPlacaAsignada",
      "aprobados",
      "rechazados",
      "sinGestion",
      "revocados",
    ];
    for (const key of otras) {
      expect(filtrosDeContador(key).hasActiveRevocationRequest).toBeUndefined();
    }
  });
});

describe("OtBandejaCountersStrip — Solicitudes de revocatoria", () => {
  it("muestra la cifra del backend y notifica la selección al pulsarla", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(
      <OtBandejaCountersStrip counters={COUNTERS} selected="" onSelect={onSelect} />,
    );

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
});

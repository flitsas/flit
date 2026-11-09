// HU #14390 — el cliente de Precios del OT: rutas del contrato, `transitOfficeId` solo cuando se pide y el organismo
// fuera del cuerpo del POST.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "../client";
import {
  fetchHistorialTarifaOt,
  fetchTarifasOt,
  fetchTiposTarifaOt,
  registrarTarifasOt,
} from "../admin-ot-portal-tarifas";

vi.mock("../client", () => ({ apiFetch: vi.fn().mockResolvedValue([]) }));

describe("admin-ot-portal-tarifas", () => {
  beforeEach(() => vi.clearAllMocks());

  it("lista con los filtros y el organismo solo para el SuperAdmin", async () => {
    await fetchTarifasOt({ soloVigentes: true }, { transitOfficeId: "ot1" });
    expect(apiFetch).toHaveBeenLastCalledWith("/api/v1/admin/ot/portal-tarifas", {
      query: { soloVigentes: true, transitOfficeId: "ot1" },
      signal: undefined,
    });
    await fetchTarifasOt({});
    expect(vi.mocked(apiFetch).mock.calls[1][1]).toMatchObject({ query: {} });
  });

  it("historial y tipos usan sus rutas", async () => {
    await fetchHistorialTarifaOt("t1", { transitOfficeId: "ot1" });
    expect(vi.mocked(apiFetch).mock.calls[0][0]).toBe(
      "/api/v1/admin/ot/portal-tarifas/t1/historial",
    );
    await fetchTiposTarifaOt();
    expect(vi.mocked(apiFetch).mock.calls[1][0]).toBe("/api/v1/admin/ot/portal-tarifas/tipos");
  });

  it("registra sin organismo en el cuerpo", async () => {
    await registrarTarifasOt(
      {
        tipoTramiteId: "x",
        organismoId: "otro",
        vigenteDesde: "2026-11-05",
        valores: [{ concepto: "CAMBIO_COLOR", valorCop: 10 }],
      },
      { transitOfficeId: "ot1" },
    );
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/admin/ot/portal-tarifas", {
      method: "POST",
      query: { transitOfficeId: "ot1" },
      body: {
        tipoTramiteId: "x",
        valores: [{ concepto: "CAMBIO_COLOR", valorCop: 10 }],
        vigenteDesde: "2026-11-05",
      },
    });
  });
});

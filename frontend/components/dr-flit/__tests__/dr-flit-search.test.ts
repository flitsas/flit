import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listInstances: vi.fn(),
    getInstance: vi.fn(),
    listTenantBiometricValidations: vi.fn(),
  },
}));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
}));

import { tramitesClient } from "@/lib/api/tramites-client";
import { fetchOtClientProcedures } from "@/lib/api/admin-ot";
import { isGuid, searchTramites, searchValidaciones } from "../dr-flit-search";

describe("dr-flit-search", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("isGuid valida UUID", () => {
    expect(isGuid("3fa85f64-5717-4562-b3fc-2c963f66afa6")).toBe(true);
    expect(isGuid("ABC123")).toBe(false);
  });

  it("busca por placa vía listInstances", async () => {
    vi.mocked(tramitesClient.listInstances).mockResolvedValue([
      {
        id: "11111111-1111-4111-a111-111111111111",
        referenceNumber: "R-1",
        modalidad: "TRASPASO",
        estado: "borrador",
        placa: "ABC123",
        vin: "VIN1",
        vehiculoMarca: null,
        vehiculoLinea: null,
        compradorNombre: null,
        compradorDocumento: null,
        organismoTransito: null,
        pasoActual: 1,
        totalPasos: 4,
        createdAt: "2026-03-01T10:00:00Z",
        draftFinalizedAt: null,
        identityValidationStatus: null,
        signaturePending: false,
        canSubmit: false,
        prioritario: false,
        tenantId: "t1",
        companiaNombre: null,
      },
    ]);

    const rows = await searchTramites("placa", "abc123", { isOtAdmin: false });
    expect(tramitesClient.listInstances).toHaveBeenCalledWith(
      expect.objectContaining({ placa: "ABC123" }),
    );
    expect(rows).toHaveLength(1);
    expect(rows[0].href).toBe("/tramites/11111111-1111-4111-a111-111111111111");
  });

  it("OT usa bandeja client-procedures", async () => {
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [
        {
          id: "22222222-2222-4222-a222-222222222222",
          clientTenantId: "c1",
          procedureTypeId: "p1",
          procedureTypeName: "Traspaso",
          referenceNumber: "OT-1",
          status: "entregado",
          createdAt: "2026-02-01T00:00:00Z",
          placa: "XYZ999",
          vin: "V2",
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });

    const rows = await searchTramites("placa", "XYZ999", { isOtAdmin: true });
    expect(fetchOtClientProcedures).toHaveBeenCalled();
    expect(rows[0].tipoTramite).toBe("Traspaso");
  });

  it("trámite sin GUID lanza error", async () => {
    await expect(
      searchTramites("tramite", "no-es-guid", { isOtAdmin: false }),
    ).rejects.toThrow(/GUID/i);
  });

  it("validaciones filtra por documento", async () => {
    vi.mocked(tramitesClient.listTenantBiometricValidations).mockResolvedValue({
      validations: [
        {
          id: "v1",
          instanceId: null,
          referenceNumber: null,
          modalidad: null,
          partyRole: null,
          name: "Ana",
          documentType: "CC",
          documentNumber: "900123",
          status: "aprobado",
          score: 90,
          provider: "mock",
          expired: false,
          createdAt: "2026-01-01T00:00:00Z",
          validatedAt: null,
          validUntil: null,
          daysRemaining: null,
          captureUrl: null,
          linkExpiresAt: null,
          email: null,
        },
      ],
      stats: { total: 1, aprobadas: 1, enProceso: 0, rechazadas: 0, expiradas: 0 },
      page: 1,
      pageSize: 20,
      total: 1,
    });

    const rows = await searchValidaciones("900123");
    expect(tramitesClient.listTenantBiometricValidations).toHaveBeenCalledWith(
      expect.objectContaining({ documentNumber: "900123" }),
    );
    expect(rows[0].name).toBe("Ana");
  });
});

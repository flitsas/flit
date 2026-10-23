import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    searchInstances: vi.fn(),
    searchNetworkInstances: vi.fn(),
    getInstance: vi.fn(),
    listTenantBiometricValidations: vi.fn(),
  },
}));

vi.mock("@/lib/api/admin-ot", () => ({
  searchOtClientProcedures: vi.fn(),
}));

import { tramitesClient } from "@/lib/api/tramites-client";
import { searchOtClientProcedures } from "@/lib/api/admin-ot";
import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";
import type { DrFlitSearchContext } from "../dr-flit-context";
import {
  DR_FLIT_PAGE_SIZE,
  esMismoRadicado,
  isGuid,
  normalizeClienteQuery,
  searchTramites,
  searchValidaciones,
} from "../dr-flit-search";

const GESTOR: DrFlitSearchContext = {
  role: "gestor",
  tenantId: "t1",
  network: { active: false },
};
const SUPERADMIN: DrFlitSearchContext = {
  role: "superadmin",
  tenantId: null,
  network: { active: false },
};
const OT: DrFlitSearchContext = {
  role: "ot_admin",
  tenantId: "ot1",
  network: { active: false },
};
const CABEZA_RED: DrFlitSearchContext = {
  role: "admin_company",
  tenantId: "t1",
  network: { active: true },
};
const CABEZA_HIJO: DrFlitSearchContext = {
  role: "admin_company",
  tenantId: "t1",
  network: { active: true, childTenantId: "hijo-1" },
};

function summary(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: "11111111-1111-4111-a111-111111111111",
    referenceNumber: "R-2026-001",
    modalidad: "TRASPASO",
    tipoNombre: "Traspaso estándar",
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
    createdAt: "2026-03-01T15:00:00Z",
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: false,
    tenantId: "t1",
    companiaNombre: "Concesionario Norte",
    ...overrides,
  };
}

describe("dr-flit-search", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("isGuid valida UUID", () => {
    expect(isGuid("3fa85f64-5717-4562-b3fc-2c963f66afa6")).toBe(true);
    expect(isGuid("ABC123")).toBe(false);
  });

  it("valor vacío no llama a ninguna API", async () => {
    const res = await searchTramites("placa", "   ", GESTOR);
    expect(res).toEqual({ items: [], total: 0 });
    expect(tramitesClient.searchInstances).not.toHaveBeenCalled();
  });

  describe("gestor (tenant propio)", () => {
    it("placa → searchInstances con placa en mayúscula, paginado y con total", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [summary()],
        total: 37,
      });

      const res = await searchTramites("placa", "abc123", GESTOR);

      expect(tramitesClient.searchInstances).toHaveBeenCalledWith(
        expect.objectContaining({
          placa: "ABC123",
          take: DR_FLIT_PAGE_SIZE,
          skip: 0,
          sortBy: "createdAt",
          sortDir: "desc",
        }),
      );
      expect(res.total).toBe(37);
      expect(res.items).toHaveLength(1);
      expect(res.items[0]).toMatchObject({
        radicado: "R-2026-001",
        tipoTramite: "Traspaso estándar",
        href: "/tramites/11111111-1111-4111-a111-111111111111",
        compania: null,
      });
    });

    it("fecha usa el formato estándar DD/MM/YYYY HH:mm en hora de Colombia", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [summary({ createdAt: "2026-03-01T15:00:00Z" })],
        total: 1,
      });
      const res = await searchTramites("vin", "vin1", GESTOR);
      // 15:00Z = 10:00 en Bogotá (UTC-5).
      expect(res.items[0].fecha).toBe("01/03/2026 10:00");
    });

    it("vin → parámetro vin en mayúscula", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({ items: [], total: 0 });
      await searchTramites("vin", "9bwzzz377vt004251", GESTOR);
      expect(tramitesClient.searchInstances).toHaveBeenCalledWith(
        expect.objectContaining({ vin: "9BWZZZ377VT004251" }),
      );
      expect(tramitesClient.searchInstances).toHaveBeenCalledWith(
        expect.not.objectContaining({ placa: expect.anything() }),
      );
    });

    it("cliente → UNA llamada con `busqueda` (nombre o documento), no comprador+vendedor", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [summary()],
        total: 1,
      });

      await searchTramites("cliente", "Ana Pérez", GESTOR);

      expect(tramitesClient.searchInstances).toHaveBeenCalledTimes(1);
      expect(tramitesClient.searchInstances).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "Ana Pérez" }),
      );
      const args = vi.mocked(tramitesClient.searchInstances).mock.calls[0]![0]!;
      expect(args).not.toHaveProperty("comprador");
      expect(args).not.toHaveProperty("vendedor");
    });

    it("HU-D — cliente por documento con puntos/espacios viaja compactado", async () => {
      expect(normalizeClienteQuery(" 1.234.567 ")).toBe("1234567");
      expect(normalizeClienteQuery("1 234 567")).toBe("1234567");
      expect(normalizeClienteQuery("Ana Pérez")).toBe("Ana Pérez");
      expect(normalizeClienteQuery("12-34")).toBe("12-34"); // <5 dígitos: no es documento

      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({ items: [], total: 0 });
      await searchTramites("cliente", "1.234.567", GESTOR);
      expect(tramitesClient.searchInstances).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "1234567" }),
      );
    });

    it("tipoTramite cae a la familia cuando no hay tipoNombre", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [summary({ tipoNombre: null, modalidad: "MATRICULAS" })],
        total: 1,
      });
      const res = await searchTramites("placa", "ABC123", GESTOR);
      expect(res.items[0].tipoTramite).toBe("Matrícula");
    });
  });

  describe("superadmin", () => {
    it("usa el mismo listado pero conserva la compañía de cada fila", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [summary()],
        total: 1,
      });
      const res = await searchTramites("placa", "ABC123", SUPERADMIN);
      expect(res.items[0].compania).toBe("Concesionario Norte");
      expect(tramitesClient.searchNetworkInstances).not.toHaveBeenCalled();
    });
  });

  describe("ot_admin", () => {
    it("usa la bandeja client-procedures con paginación y trae la compañía cliente", async () => {
      vi.mocked(searchOtClientProcedures).mockResolvedValue({
        data: [
          {
            id: "22222222-2222-4222-a222-222222222222",
            clientTenantId: "c1",
            clientTenantName: "Autos del Sur",
            procedureTypeId: "p1",
            procedureTypeName: "Traspaso",
            referenceNumber: "OT-1",
            status: "entregado",
            createdAt: "2026-02-01T00:00:00Z",
            placa: "XYZ999",
            vin: "V2",
          },
        ],
        totalCount: 5,
        page: 1,
        pageSize: 20,
      });

      const res = await searchTramites("placa", "xyz999", OT);

      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ placa: "XYZ999", page: 1, pageSize: DR_FLIT_PAGE_SIZE }),
      );
      expect(tramitesClient.searchInstances).not.toHaveBeenCalled();
      expect(res.total).toBe(5);
      expect(res.items[0]).toMatchObject({
        tipoTramite: "Traspaso",
        radicado: "OT-1",
        compania: "Autos del Sur",
      });
    });

    it("cliente → `busqueda` en la bandeja OT (HU #12218)", async () => {
      vi.mocked(searchOtClientProcedures).mockResolvedValue({
        data: [],
        totalCount: 0,
        page: 1,
        pageSize: 20,
      });
      await searchTramites("cliente", "900123456", OT);
      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "900123456" }),
      );
    });
  });

  describe("cabeza de red (AdminCompany con alcance de red)", () => {
    it("toda la red → network/instances/search sin childTenantId, con tenantName por fila", async () => {
      vi.mocked(tramitesClient.searchNetworkInstances).mockResolvedValue({
        items: [
          { ...summary({ tenantId: "hijo-1" }), tenantName: "Hija Uno", fromNetwork: true },
        ],
        total: 2,
      });

      const res = await searchTramites("placa", "abc123", CABEZA_RED);

      expect(tramitesClient.searchNetworkInstances).toHaveBeenCalledWith(
        expect.objectContaining({ placa: "ABC123", take: DR_FLIT_PAGE_SIZE }),
      );
      const args = vi.mocked(tramitesClient.searchNetworkInstances).mock.calls[0]![0]!;
      expect(args).not.toHaveProperty("childTenantId");
      expect(tramitesClient.searchInstances).not.toHaveBeenCalled();
      expect(res.total).toBe(2);
      expect(res.items[0].compania).toBe("Hija Uno");
    });

    it("un hijo concreto → childTenantId en el cuerpo", async () => {
      vi.mocked(tramitesClient.searchNetworkInstances).mockResolvedValue({
        items: [],
        total: 0,
      });
      await searchTramites("cliente", "Ana", CABEZA_HIJO);
      expect(tramitesClient.searchNetworkInstances).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "Ana", childTenantId: "hijo-1" }),
      );
    });

    it("alcance propio → listado del tenant, no la red", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({ items: [], total: 0 });
      await searchTramites("placa", "ABC123", {
        role: "admin_company",
        tenantId: "t1",
        network: { active: false },
      });
      expect(tramitesClient.searchInstances).toHaveBeenCalledTimes(1);
      expect(tramitesClient.searchNetworkInstances).not.toHaveBeenCalled();
    });
  });

  describe("trámite (HU-B: radicado o GUID)", () => {
    it("esMismoRadicado — canónico con prefijo y por consecutivo", () => {
      expect(esMismoRadicado("FT1-0000012", "ft1 0000012")).toBe(true);
      expect(esMismoRadicado("FT1-0000012", "FT1-0000012")).toBe(true);
      expect(esMismoRadicado("FT1-0000012", "12")).toBe(true);
      expect(esMismoRadicado("FT1-0000012", "0000012")).toBe(true);
      expect(esMismoRadicado("FT1-0000012", "13")).toBe(false);
      expect(esMismoRadicado("FT1-0000012", "FT2-0000012")).toBe(false);
      expect(esMismoRadicado("—", "12")).toBe(false);
    });

    it("radicado con prefijo → `busqueda` por el contexto y solo devuelve el exacto", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [
          summary({ id: "aaaaaaaa-1111-4111-a111-111111111111", referenceNumber: "FT1-0000012" }),
          // Ruido de subcadena (p. ej. el término dentro de un documento): se descarta.
          summary({ id: "bbbbbbbb-2222-4222-a222-222222222222", referenceNumber: "FT1-0000340" }),
        ],
        total: 2,
      });

      const res = await searchTramites("tramite", "ft1-0000012", GESTOR);

      expect(tramitesClient.getInstance).not.toHaveBeenCalled();
      expect(tramitesClient.searchInstances).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "ft1-0000012" }),
      );
      expect(res.total).toBe(1);
      expect(res.items.map((i) => i.radicado)).toEqual(["FT1-0000012"]);
    });

    it("solo consecutivo → casa por número final", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [
          summary({ id: "aaaaaaaa-1111-4111-a111-111111111111", referenceNumber: "FT1-0000012" }),
          summary({ id: "bbbbbbbb-2222-4222-a222-222222222222", referenceNumber: "FT1-0000120" }),
        ],
        total: 2,
      });
      const res = await searchTramites("tramite", "12", GESTOR);
      expect(res.items.map((i) => i.radicado)).toEqual(["FT1-0000012"]);
    });

    it("sin coincidencia exacta devuelve lo que trajo el servidor (no un error)", async () => {
      vi.mocked(tramitesClient.searchInstances).mockResolvedValue({
        items: [summary({ referenceNumber: "FT1-0000340" })],
        total: 1,
      });
      const res = await searchTramites("tramite", "no-es-radicado", GESTOR);
      expect(res.total).toBe(1);
      expect(res.items[0].radicado).toBe("FT1-0000340");
    });

    it("radicado respeta el contexto: OT va a la bandeja", async () => {
      vi.mocked(searchOtClientProcedures).mockResolvedValue({
        data: [],
        totalCount: 0,
        page: 1,
        pageSize: 20,
      });
      await searchTramites("tramite", "FT1-0000012", OT);
      expect(searchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ busqueda: "FT1-0000012" }),
      );
      expect(tramitesClient.searchInstances).not.toHaveBeenCalled();
    });

    it("E2E SA-09 — GUID como Super Admin (JWT con tenant propio): mensaje accionable, no error técnico", async () => {
      vi.mocked(tramitesClient.getInstance).mockRejectedValue(new Error("404 Not Found"));
      await expect(
        searchTramites("tramite", "3fa85f64-5717-4562-b3fc-2c963f66afa6", { ...SUPERADMIN, tenantId: "sa-tenant" }),
      ).rejects.toThrow(/elige primero la compañía/);
      await expect(
        searchTramites("tramite", "3fa85f64-5717-4562-b3fc-2c963f66afa6", GESTOR),
      ).rejects.toThrow(/Prueba con el radicado/);
    });

    it("con GUID pide el detalle y devuelve total 1", async () => {
      vi.mocked(tramitesClient.getInstance).mockResolvedValue({
        id: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        referenceNumber: "R-9",
        status: "aprobado",
        createdAt: "2026-05-05T12:00:00Z",
        fieldValues: [{ fieldKey: "vehiculo.placa", valueText: "kkk111" }],
      } as never);

      const res = await searchTramites("tramite", "3fa85f64-5717-4562-b3fc-2c963f66afa6", GESTOR);

      expect(res.total).toBe(1);
      expect(res.items[0]).toMatchObject({
        radicado: "R-9",
        placa: "KKK111",
        fecha: "05/05/2026 07:00",
      });
    });
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

// HU #10471 — cliente `generarImpronta` (descarga PDF + metadata en headers) y
// `describeImprontaError` (mensajes específicos por tipo de error, AC1/AC2).
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError } from "../types";

const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-token") }));
vi.mock("../client", () => ({ API_BASE_URL: "https://api.test", getToken: mocks.getToken }));

import { describeImprontaError, generarImpronta } from "../admin-improntas";

const originalFetch = global.fetch;

const validBody = {
  placa: "ABC123",
  documento: "1040326572",
  orgNombre: "Renting Demo S.A.S.",
  operador: "Ana Operadora",
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-token");
  global.URL.createObjectURL = vi.fn(() => "blob:mock");
  global.URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("generarImpronta", () => {
  it("AC1 descarga el PDF (downloadFile) y devuelve radicado/hash cuando el backend los expone en headers", async () => {
    const blob = new Blob(["%PDF-1.4"], { type: "application/pdf" });
    global.fetch = vi.fn().mockResolvedValue(
      new Response(blob, {
        status: 200,
        headers: {
          "Content-Disposition": 'attachment; filename="impronta_ABC123.pdf"',
          "X-Impronta-Radicado": "IMPR-A1B2C3D4",
          "X-Impronta-Hash": "9f1c...e0a2",
        },
      }),
    ) as never;
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    const result = await generarImpronta(validBody);

    expect(result).toEqual({ radicado: "IMPR-A1B2C3D4", hash: "9f1c...e0a2" });
  });

  it("AC1 devuelve radicado/hash en null (sin romper el flujo) cuando el backend no expone esos headers", async () => {
    const blob = new Blob(["%PDF-1.4"], { type: "application/pdf" });
    global.fetch = vi.fn().mockResolvedValue(new Response(blob, { status: 200 })) as never;
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    const result = await generarImpronta(validBody);

    expect(result).toEqual({ radicado: null, hash: null });
  });

  it("lanza ApiError con el status y el body del backend ante 4xx/5xx", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ code: "UPSTREAM_UNAVAILABLE" }), { status: 502 }),
    ) as never;

    await expect(generarImpronta(validBody)).rejects.toMatchObject({
      status: 502,
      body: { code: "UPSTREAM_UNAVAILABLE" },
    });
  });
});

describe("describeImprontaError", () => {
  it("AC2 422 VALIDATION_ERROR: usa el detalle de campo si el backend lo entrega", () => {
    const error = new ApiError(422, "Error 422", {
      code: "VALIDATION_ERROR",
      errors: [{ field: "placa", message: "La placa no tiene un formato válido." }],
    });
    expect(describeImprontaError(error)).toBe(
      "Los datos del vehículo no son válidos: La placa no tiene un formato válido.",
    );
  });

  it("AC2 422 VALIDATION_ERROR: usa un mensaje genérico si el backend no entrega detalle de campo", () => {
    const error = new ApiError(422, "Error 422", { code: "VALIDATION_ERROR" });
    expect(describeImprontaError(error)).toMatch(/no son válidos/i);
  });

  it("AC2 401 UNAUTHORIZED: mensaje de 'no autorizado' sin exponer el detalle técnico del proveedor", () => {
    const error = new ApiError(401, "Error 401", {
      code: "UNAUTHORIZED",
      message: "invalid api key kr_live_secret scope impronta:generar",
    });
    const message = describeImprontaError(error);
    expect(message).toMatch(/no autorizado/i);
    expect(message).not.toMatch(/kr_live/i);
  });

  it("AC2 502 UPSTREAM_UNAVAILABLE: mensaje de servicio no disponible, reintentable", () => {
    const error = new ApiError(502, "Error 502", { code: "UPSTREAM_UNAVAILABLE" });
    const message = describeImprontaError(error);
    expect(message).toMatch(/no está disponible/i);
    expect(message).toMatch(/intenta de nuevo/i);
  });

  it("usa un mensaje de fallback genérico para otros status de ApiError (p. ej. 500)", () => {
    const error = new ApiError(500, "Error 500", null);
    expect(describeImprontaError(error)).toMatch(/no se pudo generar la impronta/i);
  });

  it("usa un mensaje de fallback genérico ante un error de red/desconocido (no ApiError)", () => {
    expect(describeImprontaError(new TypeError("Failed to fetch"))).toMatch(
      /no se pudo conectar con el servicio de improntas/i,
    );
  });
});

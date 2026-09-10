import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError } from "../types";

// Mock del cliente para controlar token y base URL sin tocar cookies/storage. `friendlyErrorMessage`
// se reexporta real (no mockeada): es la lógica bajo prueba en los tests del Bug #11626.
const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-token") }));
vi.mock("../client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../client")>();
  return { API_BASE_URL: "https://api.test", getToken: mocks.getToken, friendlyErrorMessage: actual.friendlyErrorMessage };
});

import { downloadFile } from "../download";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-token");
  // jsdom no implementa object URLs.
  global.URL.createObjectURL = vi.fn(() => "blob:mock");
  global.URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("downloadFile", () => {
  it("adjunta el JWT, arma el query y dispara la descarga con el nombre del header", async () => {
    const blob = new Blob(["x"], { type: "application/pdf" });
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(blob, {
        status: 200,
        headers: { "Content-Disposition": 'attachment; filename="reporte.xlsx"' },
      }),
    );
    global.fetch = fetchMock as never;
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    await downloadFile("/api/v1/analytics/export/excel", {
      query: { from: "2026-06-01", to: "2026-06-26", category: undefined },
      fallbackFilename: "fallback.xlsx",
    });

    const [calledUrl, init] = fetchMock.mock.calls[0];
    expect(calledUrl).toContain("from=2026-06-01");
    expect(calledUrl).toContain("to=2026-06-26");
    expect(calledUrl).not.toContain("category"); // los undefined no se serializan
    expect((init as RequestInit).headers).toMatchObject({ Authorization: "Bearer jwt-token" });
    expect(clickSpy).toHaveBeenCalledOnce();

    clickSpy.mockRestore();
  });

  it("HU #10471 captura los headers pedidos en `captureHeaders` y los devuelve, sin romper la descarga", async () => {
    const blob = new Blob(["%PDF-1.4"], { type: "application/pdf" });
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(blob, {
        status: 200,
        headers: { "X-Impronta-Radicado": "IMPR-A1B2C3D4", "X-Impronta-Hash": "9f1c...e0a2" },
      }),
    );
    global.fetch = fetchMock as never;
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    const result = await downloadFile("/api/v1/admin/improntas/generate", {
      method: "POST",
      body: { placa: "ABC123" },
      fallbackFilename: "impronta.pdf",
      captureHeaders: ["X-Impronta-Radicado", "X-Impronta-Hash", "X-Not-Present"],
    });

    expect(result).toEqual({
      "X-Impronta-Radicado": "IMPR-A1B2C3D4",
      "X-Impronta-Hash": "9f1c...e0a2",
      "X-Not-Present": null,
    });
    expect(clickSpy).toHaveBeenCalledOnce();

    clickSpy.mockRestore();
  });

  it("lanza ApiError con el status del backend ante 4xx/5xx", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response('{"message":"boom"}', { status: 500 }),
    ) as never;

    await expect(
      downloadFile("/api/v1/analytics/export/excel", { fallbackFilename: "f.xlsx" }),
    ).rejects.toMatchObject({ status: 500 });
    await expect(
      downloadFile("/api/v1/analytics/export/excel", { fallbackFilename: "f.xlsx" }),
    ).rejects.toBeInstanceOf(ApiError);
  });

  it("Bug #11626 — el mensaje sale del detail del backend, nunca interpola la ruta exportada", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "El rango de fechas supera el máximo permitido." }), {
        status: 422,
      }),
    ) as never;

    let caught: unknown;
    try {
      await downloadFile("/api/v1/analytics/export/excel", { fallbackFilename: "f.xlsx" });
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.message).toBe("El rango de fechas supera el máximo permitido.");
    expect(err.message).not.toContain("/api/v1/analytics/export/excel");
    expect(err.message).not.toMatch(/^Error \d+/);
  });

  it("Bug #11626 — sin cuerpo JSON cae al mensaje genérico, sin ruta ni status crudos", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 500 })) as never;

    let caught: unknown;
    try {
      await downloadFile("/api/v1/analytics/export/excel", { fallbackFilename: "f.xlsx" });
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    expect((caught as ApiError).message).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
  });
});

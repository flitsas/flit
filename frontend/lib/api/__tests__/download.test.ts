import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError } from "../types";

// Mock del cliente para controlar token y base URL sin tocar cookies/storage.
const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-token") }));
vi.mock("../client", () => ({ API_BASE_URL: "https://api.test", getToken: mocks.getToken }));

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
});

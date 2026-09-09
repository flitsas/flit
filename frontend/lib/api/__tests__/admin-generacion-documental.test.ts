// HU-01 (Feature #12201) — cliente del módulo "Generación documental": rutas del contrato
// §7.1, filtro de estado con el colapso pending+processing (CF-21) y generación que NUNCA
// devuelve el binario.
// Uso de ejemplo: await fetchStandaloneDocuments({ page: 1, pageSize: 20 })
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-token") }));
vi.mock("../client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../client")>();
  return { ...actual, API_BASE_URL: "https://api.test", getToken: mocks.getToken };
});

import {
  GENERACION_DOCUMENTAL_API_BASE,
  fetchStandaloneDocuments,
  generateRuesDocument,
  previewRuesCompany,
  requestStandaloneDocumentDownload,
} from "../admin-generacion-documental";
import { standaloneDocumentStatusQuery } from "@/components/admin/generacion-documental/status-labels";

const originalFetch = global.fetch;

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function lastUrl(): URL {
  const call = (global.fetch as unknown as ReturnType<typeof vi.fn>).mock.calls.at(-1);
  return new URL(String(call?.[0]));
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-token");
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("fetchStandaloneDocuments", () => {
  it("llama a la ruta base del módulo con paginación", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ items: [], page: 1, pageSize: 20, total: 0 }),
    ) as never;

    const result = await fetchStandaloneDocuments({ page: 1, pageSize: 20 });

    const url = lastUrl();
    expect(url.pathname).toBe(GENERACION_DOCUMENTAL_API_BASE);
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(result.items).toEqual([]);
  });

  it("CF-21: el filtro «En proceso» viaja como los DOS estados internos", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ items: [], page: 1, pageSize: 20, total: 0 }),
    ) as never;

    await fetchStandaloneDocuments({ status: standaloneDocumentStatusQuery("en_proceso") });

    expect(lastUrl().searchParams.getAll("status")).toEqual(["pending", "processing"]);
  });

  it("no envía parámetros vacíos ni indefinidos", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ items: [], page: 1, pageSize: 20, total: 0 }),
    ) as never;

    await fetchStandaloneDocuments({});

    expect(lastUrl().search).toBe("");
  });

  it("contrato: la respuesta trae items, page, pageSize y total", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        items: [
          {
            id: "1",
            documentType: "certificado_rues",
            scenario: null,
            status: "generated",
            createdAt: "2026-09-01T10:00:00Z",
          },
        ],
        page: 2,
        pageSize: 20,
        total: 21,
      }),
    ) as never;

    const result = await fetchStandaloneDocuments({ page: 2 });

    expect(result).toHaveProperty("items");
    expect(result).toHaveProperty("page", 2);
    expect(result).toHaveProperty("pageSize", 20);
    expect(result).toHaveProperty("total", 21);
    expect(result.items[0]).not.toHaveProperty("documentSnapshot");
  });
});

describe("requestStandaloneDocumentDownload", () => {
  it("pide la presigned URL en GET /{id}/download", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ url: "https://s3.test/doc.pdf?sig=x", expiresAt: "2026-09-01T10:05:00Z" }),
    ) as never;

    const link = await requestStandaloneDocumentDownload("abc-123");

    const [url, init] = (global.fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(new URL(String(url)).pathname).toBe(`${GENERACION_DOCUMENTAL_API_BASE}/abc-123/download`);
    expect((init as RequestInit).method).toBe("GET");
    expect(link).toEqual({ url: "https://s3.test/doc.pdf?sig=x", expiresAt: "2026-09-01T10:05:00Z" });
  });

  it("propaga el 404 de un documento de otro tenant sin exponer detalle", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ message: "not_found" }, 404)) as never;

    await expect(requestStandaloneDocumentDownload("otro-tenant")).rejects.toThrow();
  });
});

describe("previewRuesCompany / generateRuesDocument", () => {
  it("preview: POST con el NIT en el cuerpo", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ found: true, nit: "900123456", fields: [] }),
    ) as never;

    await previewRuesCompany("900123456");

    const [url, init] = (global.fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(new URL(String(url)).pathname).toBe(`${GENERACION_DOCUMENTAL_API_BASE}/rues/preview`);
    expect((init as RequestInit).method).toBe("POST");
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({ nit: "900123456" });
  });

  it("generate: responde { id, status } en JSON y nunca un binario PDF", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ id: "doc-1", status: "generated" }),
    ) as never;

    const result = await generateRuesDocument("900123456");

    expect(result).toEqual({ id: "doc-1", status: "generated" });
    const [, init] = (global.fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers["Content-Type"]).toBe("application/json");
  });
});

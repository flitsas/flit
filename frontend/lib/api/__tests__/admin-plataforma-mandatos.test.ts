import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError } from "../types";

// Mock del cliente para controlar el token sin tocar cookies/storage. Las tres funciones bajo
// prueba (`uploadMandateOtPdfTemplate`, `extractMandateConfigFromFile`, `fetchMandateOtPreview`)
// usan fetch directo (multipart/binario) y no pasan por `apiFetch` — `friendlyErrorMessage` real.
const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-token") }));
vi.mock("../client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../client")>();
  return { ...actual, getToken: mocks.getToken };
});

// Uso de ejemplo:
// await uploadMandateOtPdfTemplate("office-1", file) → MandateOtConfigView
// Ante 4xx/5xx no-ok lanza ApiError cuyo mensaje sale del ProblemDetails del backend, nunca de
// "Error {status} al subir plantilla" / "al extraer mandato" / "al previsualizar mandato" (Bug #11626).
import {
  uploadMandateOtPdfTemplate,
  extractMandateConfigFromFile,
  fetchMandateOtPreview,
} from "../admin-plataforma-mandatos";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-token");
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("uploadMandateOtPdfTemplate — errores no-ok (Bug #11626)", () => {
  it("happy path: 200 devuelve la config mapeada", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ officeId: "o1" }), { status: 200 })) as never;

    const result = await uploadMandateOtPdfTemplate("o1", new File(["%PDF"], "plantilla.pdf"));

    expect(result.officeId).toBe("o1");
  });

  it("edge case — 413 con detail: el mensaje es el detail, sin 'al subir plantilla'", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "El archivo supera el tamaño máximo permitido." }), { status: 413 }),
    ) as never;

    let caught: unknown;
    try {
      await uploadMandateOtPdfTemplate("o1", new File(["%PDF"], "plantilla.pdf"));
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.status).toBe(413);
    expect(err.message).toBe("El archivo supera el tamaño máximo permitido.");
    expect(err.message).not.toMatch(/al subir plantilla/);
  });

  it("contrato — sin cuerpo JSON cae al mensaje genérico", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 500 })) as never;

    await expect(uploadMandateOtPdfTemplate("o1", new File(["%PDF"], "p.pdf"))).rejects.toMatchObject({
      status: 500,
      message: "No se pudo completar la solicitud. Inténtalo de nuevo.",
    });
  });
});

describe("extractMandateConfigFromFile — errores no-ok (Bug #11626)", () => {
  it("happy path: 200 devuelve el resultado extraído", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ suggestedTemplateCode: "generico" }), { status: 200 }),
    ) as never;

    const result = await extractMandateConfigFromFile(new File(["x"], "mandato.pdf"));

    expect(result.suggestedTemplateCode).toBe("generico");
  });

  it("edge case — 422 con title (sin detail): usa el title, sin 'al extraer mandato'", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ title: "No se pudo leer el PDF." }), { status: 422 }),
    ) as never;

    let caught: unknown;
    try {
      await extractMandateConfigFromFile(new File(["x"], "mandato.pdf"));
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.message).toBe("No se pudo leer el PDF.");
    expect(err.message).not.toMatch(/al extraer mandato/);
  });
});

describe("fetchMandateOtPreview — errores no-ok (Bug #11626)", () => {
  it("happy path: 200 devuelve un Blob PDF", async () => {
    const blob = new Blob(["%PDF-1.4"], { type: "application/pdf" });
    global.fetch = vi.fn().mockResolvedValue(new Response(blob, { status: 200 })) as never;

    const result = await fetchMandateOtPreview("o1");

    expect(result.type).toBe("application/pdf");
  });

  it("contrato — 409 con error: el mensaje sale de { error }, nunca de la ruta interna", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "La configuración del OT está incompleta." }), { status: 409 }),
    ) as never;

    let caught: unknown;
    try {
      await fetchMandateOtPreview("o1");
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.message).toBe("La configuración del OT está incompleta.");
    expect(err.message).not.toContain("/api/v1/admin/plataforma/mandatos");
    expect(err.message).not.toMatch(/al previsualizar mandato/);
  });
});

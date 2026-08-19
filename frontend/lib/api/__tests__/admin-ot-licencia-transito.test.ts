import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError } from "../types";

// Mock del cliente para controlar el token sin tocar cookies/storage. `friendlyErrorMessage` y
// `apiFetch` se reexportan reales — este archivo solo prueba `adjuntarOtLicenciaTransito`, que usa
// fetch directo (multipart) y NO puede pasar por `apiFetch` (JSON-only).
const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-token") }));
vi.mock("../client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../client")>();
  return { ...actual, getToken: mocks.getToken };
});

// Uso de ejemplo:
// await adjuntarOtLicenciaTransito("proc-1", file) → OtProcedureAttachment
// Ante 4xx/5xx no-ok lanza ApiError cuyo mensaje sale del ProblemDetails del backend, nunca de
// "Error {status} al adjuntar la LT" (Bug #11626).
import { adjuntarOtLicenciaTransito } from "../admin-ot";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-token");
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("adjuntarOtLicenciaTransito — errores no-ok (Bug #11626)", () => {
  it("happy path: 200 devuelve el attachment", async () => {
    const attachment = { id: "att-1", fileName: "lt.pdf" };
    global.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(attachment), { status: 200 })) as never;

    const result = await adjuntarOtLicenciaTransito("proc-1", new File(["x"], "lt.pdf"));

    expect(result).toEqual(attachment);
  });

  it("edge case — 422 con detail: el mensaje es el detail del backend, sin 'al adjuntar la LT'", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "El archivo debe ser PDF." }), { status: 422 }),
    ) as never;

    let caught: unknown;
    try {
      await adjuntarOtLicenciaTransito("proc-1", new File(["x"], "lt.pdf"));
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.status).toBe(422);
    expect(err.message).toBe("El archivo debe ser PDF.");
    expect(err.message).not.toMatch(/al adjuntar la LT/);
  });

  it("contrato — sin cuerpo JSON cae al mensaje genérico y conserva status/body en el ApiError", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 500 })) as never;

    let caught: unknown;
    try {
      await adjuntarOtLicenciaTransito("proc-1", new File(["x"], "lt.pdf"));
    } catch (e) {
      caught = e;
    }

    expect(caught).toBeInstanceOf(ApiError);
    const err = caught as ApiError;
    expect(err.status).toBe(500);
    expect(err.message).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
    expect(err.body).toBeNull();
  });
});

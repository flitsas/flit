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
  mapCompanyRule,
  uploadMandateOtPdfTemplate,
  extractMandateConfigFromFile,
  fetchMandateOtPreview,
  listMandatoFormats,
  updateMandatoFormat,
  getMandatoFormat,
  previewMandatoFormatDraft,
  readFormatError,
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

describe("mapCompanyRule — rowVersion (HU #13150)", () => {
  it("conserva rowVersion en camelCase y PascalCase", () => {
    expect(mapCompanyRule({ companyTenantId: "a", rowVersion: 4 }).rowVersion).toBe(4);
    expect(mapCompanyRule({ CompanyTenantId: "a", RowVersion: "7" }).rowVersion).toBe(7);
  });

  it("es null cuando la compañía hereda (sin regla)", () => {
    expect(mapCompanyRule({ companyTenantId: "a", rowVersion: null }).rowVersion).toBeNull();
    expect(mapCompanyRule({ companyTenantId: "a" }).rowVersion).toBeNull();
  });
});

describe("listMandatoFormats (HU #13174)", () => {
  it("pide /mandatos/formatos y mapea el catálogo", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          items: [
            { code: "auto", name: "Automática", assignmentMode: "signer", baseRedaction: null, selectableAsRedaction: false, delegatesToOfficeTemplate: true },
            { code: "bello", name: "Bello", assignmentMode: "signer", baseRedaction: "bello", selectableAsRedaction: true, delegatesToOfficeTemplate: false },
          ],
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    global.fetch = fetchMock as never;

    const result = await listMandatoFormats();

    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/v1/admin/plataforma/mandatos/formatos");
    expect(result.map((f) => f.code)).toEqual(["auto", "bello"]);
    expect(result[0]).toMatchObject({ baseRedaction: null, delegatesToOfficeTemplate: true });
    expect(result[1].selectableAsRedaction).toBe(true);
  });

  it("sin items devuelve una lista vacía", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }),
    ) as never;
    expect(await listMandatoFormats()).toEqual([]);
  });
});

describe("edición de formatos (HU #13175)", () => {
  const json = (data: unknown, status = 200) =>
    new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

  it("updateMandatoFormat hace PUT con rowVersion y devuelve el formato y la versión publicada", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      json({ format: { code: "bello", name: "Bello 2", rowVersion: 5, currentVersion: 3 }, changed: true, publishedVersion: 3 }),
    );
    global.fetch = fetchMock as never;
    const r = await updateMandatoFormat("bello", { rowVersion: 4, name: "Bello 2" });
    expect(String(fetchMock.mock.calls[0][0])).toContain("/mandatos/formatos/bello");
    expect(fetchMock.mock.calls[0][1].method).toBe("PUT");
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ rowVersion: 4, name: "Bello 2" });
    expect(r).toMatchObject({ changed: true, publishedVersion: 3 });
    expect(r.format).toMatchObject({ rowVersion: 5, currentVersion: 3 });
  });

  it("un 409 llega como ApiError con el cuerpo", async () => {
    global.fetch = vi.fn().mockResolvedValue(json({ error: "row_version_conflict" }, 409)) as never;
    await expect(updateMandatoFormat("bello", { rowVersion: 1 })).rejects.toMatchObject({ status: 409 });
  });

  it("getMandatoFormat mapea cuerpo y versiones", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      json({
        format: { code: "bello", name: "Bello" },
        body: "texto",
        versions: [{ versionNumber: 1, sha256: "a", createdAt: "2026-09-01T00:00:00Z", createdBy: "u" }],
      }),
    ) as never;
    const d = await getMandatoFormat("bello");
    expect(d.body).toBe("texto");
    expect(d.versions[0]).toMatchObject({ versionNumber: 1, createdBy: "u" });
  });

  it("previewMandatoFormatDraft envía el borrador por POST y falla con el cuerpo del error", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      json({ error: "plantilla_variable_invalida", unknownVariables: [{ name: "x", line: 1, column: 2 }] }, 400),
    );
    global.fetch = fetchMock as never;
    const err = await previewMandatoFormatDraft("bello", "{{x}}").catch((e) => e);
    expect(fetchMock.mock.calls[0][1].method).toBe("POST");
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ body: "{{x}}" });
    expect(readFormatError(err.body)).toEqual({
      error: "plantilla_variable_invalida",
      unknownVariables: [{ name: "x", line: 1, column: 2 }],
    });
  });
});

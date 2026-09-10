// HU #11315 (Feature #11309, ADR-0042) — cliente de documentos personalizados por compañía. Cubre:
// (1) `fetchPersonalizedDocuments` filtra `pendiente`/`rechazado` del historial visible; (2)
// `uploadAndConfirmPersonalizedDocument` orquesta create→upload→confirm y sube el PDF con los campos
// del multipart ANTES del `file`; (3) los detectores de 409 (`canal_no_habilitado` /
// `version_no_activable`) y el extractor de errores 422 tipados.
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

import { ApiError, ApiValidationError } from "../types";
import {
  fetchPersonalizedDocuments,
  firstValidationError,
  isChannelNotEnabled,
  isVersionNotActivable,
  uploadAndConfirmPersonalizedDocument,
  uploadPersonalizedDocumentFile,
  type PersonalizedDocumentVersion,
} from "../admin-personalized-documents";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";
const originalFetch = global.fetch;

function version(overrides: Partial<PersonalizedDocumentVersion>): PersonalizedDocumentVersion {
  return {
    id: "v1",
    version: 1,
    status: "activo",
    isActive: true,
    filename: "doc.pdf",
    sha256: "hash",
    pageCount: 3,
    createdAt: "2026-08-01T00:00:00Z",
    createdBy: null,
    activatedAt: "2026-08-01T00:00:00Z",
    activatedBy: null,
    deactivatedAt: null,
    deactivatedBy: null,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("fetchPersonalizedDocuments", () => {
  it("filtra del historial las versiones `pendiente` y `rechazado` (el archivo rechazado no aparece)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          documents: [
            {
              documentType: "mandato",
              active: version({ id: "v3", version: 3, status: "activo", isActive: true }),
              history: [
                version({ id: "v3", version: 3, status: "activo", isActive: true }),
                version({ id: "v2", version: 2, status: "historico", isActive: false }),
                version({ id: "v-rejected", version: 4, status: "rechazado", isActive: false }),
                version({ id: "v-pending", version: 5, status: "pendiente", isActive: false }),
              ],
            },
          ],
        }),
        { status: 200 },
      ),
    ) as never;

    const result = await fetchPersonalizedDocuments(TENANT);

    expect(result).toHaveLength(1);
    const ids = result[0].history.map((v) => v.id);
    expect(ids).toEqual(["v3", "v2"]);
    expect(ids).not.toContain("v-rejected");
    expect(ids).not.toContain("v-pending");
  });
});

describe("uploadAndConfirmPersonalizedDocument", () => {
  it("orquesta create → upload directo a storage → confirm, con los campos ANTES del file", async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      calls.push({ url: url.toString(), init });
      if (calls.length === 1) {
        // POST create
        return new Response(
          JSON.stringify({
            id: "new-id",
            version: 1,
            upload: { storagePath: "sp-1", url: "https://storage.test/upload", fields: { key: "sp-1", policy: "xyz" } },
          }),
          { status: 201 },
        );
      }
      if (calls.length === 2) {
        // Subida directa a storage
        return new Response(null, { status: 204 });
      }
      // POST confirm
      return new Response(
        JSON.stringify({ id: "new-id", version: 1, status: "activo", sha256: "recalculado", pageCount: 5 }),
        { status: 200 },
      );
    }) as never;

    const file = new File(["%PDF-1.4 contenido"], "mandato.pdf", { type: "application/pdf" });
    const result = await uploadAndConfirmPersonalizedDocument(TENANT, "mandato", file);

    expect(result).toEqual({ id: "new-id", version: 1, status: "activo", sha256: "recalculado", pageCount: 5 });
    expect(calls).toHaveLength(3);
    expect(calls[0].url).toContain(`/api/v1/admin/companies/${TENANT}/personalized-documents`);
    expect(calls[1].url).toBe("https://storage.test/upload");
    expect(calls[2].url).toContain("/new-id/confirm");

    // Los campos firmados van ANTES del `file` en el FormData de la subida a storage.
    const uploadBody = calls[1].init?.body as FormData;
    const keys = Array.from(uploadBody.keys());
    expect(keys.indexOf("key")).toBeLessThan(keys.indexOf("file"));
    expect(keys.indexOf("policy")).toBeLessThan(keys.indexOf("file"));
  });

  it("propaga ApiValidationError (422) cuando `confirm` rechaza el PDF", async () => {
    let call = 0;
    global.fetch = vi.fn(async () => {
      call += 1;
      if (call === 1) {
        return new Response(
          JSON.stringify({
            id: "new-id",
            version: 1,
            upload: { storagePath: "sp-1", url: "https://storage.test/upload", fields: {} },
          }),
          { status: 201 },
        );
      }
      if (call === 2) {
        return new Response(null, { status: 204 });
      }
      return new Response(
        JSON.stringify({
          errors: [{ field: "file", code: "excede_paginas", message: "El PDF excede el máximo de 30 páginas." }],
        }),
        { status: 422 },
      );
    }) as never;

    const file = new File(["%PDF-1.4 contenido"], "mandato.pdf", { type: "application/pdf" });
    await expect(uploadAndConfirmPersonalizedDocument(TENANT, "mandato", file)).rejects.toBeInstanceOf(
      ApiValidationError,
    );
  });

  it("lanza un Error legible si la subida directa a storage falla", async () => {
    let call = 0;
    global.fetch = vi.fn(async () => {
      call += 1;
      if (call === 1) {
        return new Response(
          JSON.stringify({
            id: "new-id",
            version: 1,
            upload: { storagePath: "sp-1", url: "https://storage.test/upload", fields: {} },
          }),
          { status: 201 },
        );
      }
      return new Response("Access Denied", { status: 403 });
    }) as never;

    const file = new File(["%PDF-1.4 contenido"], "mandato.pdf", { type: "application/pdf" });
    await expect(uploadAndConfirmPersonalizedDocument(TENANT, "mandato", file)).rejects.toThrow(/403/);
  });
});

describe("uploadPersonalizedDocumentFile", () => {
  it("agrega los campos del ticket ANTES de `file` en el FormData", async () => {
    let captured: FormData | null = null;
    global.fetch = vi.fn(async (_url: string | URL, init?: RequestInit) => {
      captured = init?.body as FormData;
      return new Response(null, { status: 204 });
    }) as never;

    const file = new File(["contenido"], "doc.pdf", { type: "application/pdf" });
    await uploadPersonalizedDocumentFile(
      { storagePath: "sp", url: "https://storage.test/x", fields: { a: "1", b: "2" } },
      file,
    );

    expect(captured).not.toBeNull();
    const keys = Array.from((captured as unknown as FormData).keys());
    expect(keys).toEqual(["a", "b", "file"]);
  });
});

describe("detectores de error 409 y extractor de error 422", () => {
  it("isChannelNotEnabled reconoce el 409 canal_no_habilitado", () => {
    const err = new ApiError(409, "conflict", { error: "canal_no_habilitado", message: "..." });
    expect(isChannelNotEnabled(err)).toBe(true);
    expect(isVersionNotActivable(err)).toBe(false);
  });

  it("isVersionNotActivable reconoce el 409 version_no_activable", () => {
    const err = new ApiError(409, "conflict", { error: "version_no_activable", message: "..." });
    expect(isVersionNotActivable(err)).toBe(true);
    expect(isChannelNotEnabled(err)).toBe(false);
  });

  it("ninguno de los dos confunde un ApiError genérico (404) con un 409 propio", () => {
    const err = new ApiError(404, "not found");
    expect(isChannelNotEnabled(err)).toBe(false);
    expect(isVersionNotActivable(err)).toBe(false);
  });

  it("firstValidationError expone field/code/message del primer error", () => {
    const err = new ApiValidationError(
      // El backend manda `code` aunque el tipo ValidationError genérico del cliente no lo declare.
      [{ field: "file", message: "El PDF está cifrado." } as never],
      422,
    );
    (err.errors[0] as unknown as { code: string }).code = "pdf_cifrado";
    const detail = firstValidationError(err);
    expect(detail).toEqual({ field: "file", code: "pdf_cifrado", message: "El PDF está cifrado." });
  });

  it("firstValidationError devuelve null si no hay errores", () => {
    expect(firstValidationError(new ApiValidationError([], 422))).toBeNull();
  });
});

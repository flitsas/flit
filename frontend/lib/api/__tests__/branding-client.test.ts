// HU #12431 AC1 — cliente de la muestra de correo con el tema de la red (autogestión de la
// cabeza). Cubre: (1) query string exacto (`templateId`/`source`), (2) default de `templateId`
// (no se manda si el caller no lo pasa — el backend usa su propio default), (3) 404
// BRANDING_NOT_FOUND y 400 BRANDING_SAMPLE_TEMPLATE_NOT_ALLOWED tipados como ApiError.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../types";
import { getCompanyBrandingEmailSample } from "../branding-client";

const originalFetch = global.fetch;

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  global.fetch = originalFetch;
});

function sampleResponse(overrides: Record<string, unknown> = {}) {
  return {
    templateId: "tramites.aprobado",
    subject: "Tu trámite fue aprobado",
    html: "<p>Hola</p>",
    theme: { kind: "brand", platformName: "Movilidad Andina", version: 3, senderName: "Movilidad Andina" },
    ...overrides,
  };
}

describe("getCompanyBrandingEmailSample — query string exacto", () => {
  it("arma templateId y source en el query string", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(sampleResponse()), { status: 200 });
    }) as never;

    await getCompanyBrandingEmailSample({ templateId: "tramites.aprobado", source: "draft" });

    expect(capturedUrl).toContain("/api/v1/company/branding/email-sample");
    expect(capturedUrl).toContain("templateId=tramites.aprobado");
    expect(capturedUrl).toContain("source=draft");
  });

  it("sin templateId no manda el parámetro (el backend usa su default)", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(sampleResponse()), { status: 200 });
    }) as never;

    await getCompanyBrandingEmailSample({ source: "published" });

    expect(capturedUrl).not.toContain("templateId=");
    expect(capturedUrl).toContain("source=published");
  });

  it("devuelve el theme aditivo tal cual", async () => {
    global.fetch = vi.fn(async () => new Response(JSON.stringify(sampleResponse()), { status: 200 })) as never;

    const result = await getCompanyBrandingEmailSample({ source: "draft" });

    expect(result.theme).toEqual({
      kind: "brand",
      platformName: "Movilidad Andina",
      version: 3,
      senderName: "Movilidad Andina",
    });
  });
});

describe("getCompanyBrandingEmailSample — errores tipados", () => {
  it("404 BRANDING_NOT_FOUND se propaga como ApiError con status 404", async () => {
    global.fetch = vi.fn(
      async () => new Response(JSON.stringify({ error: "BRANDING_NOT_FOUND" }), { status: 404 }),
    ) as never;

    await expect(getCompanyBrandingEmailSample({ source: "published" })).rejects.toMatchObject({
      status: 404,
    });
    await expect(getCompanyBrandingEmailSample({ source: "published" })).rejects.toBeInstanceOf(ApiError);
  });

  it("400 BRANDING_SAMPLE_TEMPLATE_NOT_ALLOWED se propaga como ApiError con status 400", async () => {
    global.fetch = vi.fn(
      async () =>
        new Response(JSON.stringify({ error: "BRANDING_SAMPLE_TEMPLATE_NOT_ALLOWED" }), { status: 400 }),
    ) as never;

    await expect(
      getCompanyBrandingEmailSample({ templateId: "otra.plantilla", source: "draft" }),
    ).rejects.toMatchObject({ status: 400, message: "BRANDING_SAMPLE_TEMPLATE_NOT_ALLOWED" });
  });

  it("403 (hija/Concesión/sin red) se propaga como ApiError", async () => {
    global.fetch = vi.fn(async () => new Response(null, { status: 403 })) as never;

    await expect(getCompanyBrandingEmailSample({ source: "draft" })).rejects.toMatchObject({
      status: 403,
    });
  });
});

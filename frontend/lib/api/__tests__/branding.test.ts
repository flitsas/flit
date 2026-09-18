// HU #12414 — cliente de identidad de marca. Cubre: (1) `source` decide la base de endpoints
// (company vs admin/{tenantId}), (2) `retire` solo existe del lado admin, (3) el upload de logo va
// multipart directo (mismo patrón que admin-banners), (4) los códigos de error de marca se leen
// del envelope `{ error }`.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../types";
import {
  brandingErrorCode,
  getBranding,
  isBrandingNotFound,
  publishBranding,
  retireBranding,
  upsertBrandingDraft,
  uploadBrandLogo,
  type TenantBrandingResponse,
} from "../branding";

const originalFetch = global.fetch;

function branding(overrides: Partial<TenantBrandingResponse> = {}): TenantBrandingResponse {
  return {
    tenantId: "tenant-1",
    draft: { platformName: "Movilidad Andina", colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" }, logoId: null },
    published: null,
    publishedVersion: 0,
    publishedAt: null,
    publishedBy: null,
    hasUnpublishedChanges: true,
    logoUrl: null,
    completeness: { isComplete: false, missing: ["logo"] },
    rowVersion: 1,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("getBranding — base de endpoints por source (AC6)", () => {
  it("source=company usa /api/v1/company/branding", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(branding()), { status: 200 });
    }) as never;

    await getBranding("company");

    expect(capturedUrl).toContain("/api/v1/company/branding");
    expect(capturedUrl).not.toContain("/admin/");
  });

  it("source=admin usa /api/v1/admin/companies/{tenantId}/branding", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(branding()), { status: 200 });
    }) as never;

    await getBranding("admin", "tenant-42");

    expect(capturedUrl).toContain("/api/v1/admin/companies/tenant-42/branding");
  });

  it("source=admin sin tenantId lanza (uso incorrecto del componente)", async () => {
    await expect(getBranding("admin")).rejects.toThrow(/tenantId/);
  });
});

describe("isBrandingNotFound", () => {
  it("true para 404 (aún sin configuración inicial)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ error: "BRANDING_NOT_FOUND" }), { status: 404 })) as never;
    const err = await getBranding("company").catch((e) => e);
    expect(isBrandingNotFound(err)).toBe(true);
  });

  it("false para otros status", () => {
    expect(isBrandingNotFound(new ApiError(500, "boom"))).toBe(false);
    expect(isBrandingNotFound(new Error("otro"))).toBe(false);
  });
});

describe("brandingErrorCode", () => {
  it("lee el código del envelope { error } de un ApiError", () => {
    const err = new ApiError(422, "Contraste insuficiente", { error: "BRANDING_CONTRAST_TOO_LOW" });
    expect(brandingErrorCode(err)).toBe("BRANDING_CONTRAST_TOO_LOW");
  });

  it("retorna null si no es un ApiError o no trae código", () => {
    expect(brandingErrorCode(new Error("otro"))).toBeNull();
    expect(brandingErrorCode(new ApiError(500, "boom"))).toBeNull();
  });
});

describe("upsertBrandingDraft", () => {
  it("hace PUT con el body del borrador contra la base correcta", async () => {
    let capturedMethod = "";
    let capturedBody: unknown = null;
    global.fetch = vi.fn(async (_url: string | URL, init?: RequestInit) => {
      capturedMethod = init?.method ?? "";
      capturedBody = init?.body ? JSON.parse(init.body as string) : null;
      return new Response(JSON.stringify(branding()), { status: 200 });
    }) as never;

    await upsertBrandingDraft("company", { platformName: "Movilidad Andina", colors: null, logoId: null, rowVersion: 1 });

    expect(capturedMethod).toBe("PUT");
    expect(capturedBody).toMatchObject({ platformName: "Movilidad Andina", rowVersion: 1 });
  });

  it("propaga BRANDING_CONTRAST_TOO_LOW en un 422 (AC3: bloquea publicar, no guardar)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "BRANDING_CONTRAST_TOO_LOW", message: "Contraste insuficiente" }), { status: 422 }),
    ) as never;

    await expect(
      upsertBrandingDraft("company", { platformName: "X", colors: null, logoId: null, rowVersion: 1 }),
    ).rejects.toBeInstanceOf(ApiError);
  });
});

describe("publishBranding", () => {
  it("hace POST a .../publish con rowVersion", async () => {
    let capturedUrl = "";
    let capturedBody: unknown = null;
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedBody = init?.body ? JSON.parse(init.body as string) : null;
      return new Response(JSON.stringify(branding({ publishedVersion: 1 })), { status: 200 });
    }) as never;

    const result = await publishBranding("admin", 4, "tenant-42");

    expect(capturedUrl).toContain("/admin/companies/tenant-42/branding/publish");
    expect(capturedBody).toEqual({ rowVersion: 4 });
    expect(result.publishedVersion).toBe(1);
  });

  it("propaga BRANDING_INCOMPLETE en 422 (AC5: borrador incompleto no publica)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "BRANDING_INCOMPLETE", details: { missing: ["logo"] } }), { status: 422 }),
    ) as never;

    const err = await publishBranding("company", 1).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(brandingErrorCode(err)).toBe("BRANDING_INCOMPLETE");
  });
});

describe("retireBranding — solo existe del lado admin (AC6)", () => {
  it("hace POST a /admin/companies/{tenantId}/branding/retire", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(JSON.stringify(branding({ published: null })), { status: 200 });
    }) as never;

    await retireBranding("tenant-42");

    expect(capturedUrl).toContain("/admin/companies/tenant-42/branding/retire");
  });
});

describe("uploadBrandLogo", () => {
  it("envía multipart/form-data DIRECTO (mismo patrón que admin-banners)", async () => {
    let capturedUrl = "";
    let capturedForm: FormData | null = null;
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedForm = init?.body as FormData;
      return new Response(
        JSON.stringify({
          logoId: "logo-1",
          version: 1,
          contentType: "image/png",
          width: 300,
          height: 100,
          sizeBytes: 1024,
          sha256: "abc",
          logoUrl: "/api/v1/public/branding/logos/logo-1",
        }),
        { status: 201 },
      );
    }) as never;

    const file = new File(["img"], "logo.png", { type: "image/png" });
    const result = await uploadBrandLogo("company", file);

    expect(capturedUrl).toContain("/api/v1/company/branding/logo");
    expect((capturedForm as unknown as FormData).get("file")).toBe(file);
    expect(result.logoId).toBe("logo-1");
  });

  it("traduce un rechazo 422 del servidor a ApiError con el código de marca", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "BRANDING_LOGO_TOO_LARGE" }), { status: 422 }),
    ) as never;

    const file = new File(["img"], "logo.png", { type: "image/png" });
    const err = await uploadBrandLogo("company", file).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(brandingErrorCode(err)).toBe("BRANDING_LOGO_TOO_LARGE");
  });
});

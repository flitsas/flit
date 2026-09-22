// Bug #12735 — "el logotipo de Marca Blanca guarda pero no se carga". El backend devuelve
// `logoUrl` relativa (`/api/v1/public/branding/logos/{logoId}`); en un host FLIT (consola
// SuperAdmin en `*.flitsas.online`) la API es cross-origin y un `<img src>` relativo apuntaba al
// host del front. Cubre: (1) con `NEXT_PUBLIC_API_BASE_URL` de host FLIT la URL sale absoluta
// contra la API sin duplicar `/api/v1`, (2) sin base (host de red / dev local) cae a
// `window.location.origin`, (3) una URL ya absoluta no se duplica, (4) acepta un `logoId` pelado,
// (5) `getBranding` / `upsertBrandingDraft` / `uploadBrandLogo` devuelven `logoUrl` ya absoluta.
//
// Uso de ejemplo:
//   brandLogoUrl("/api/v1/public/branding/logos/logo-1")
//   // → "https://api.qa.flitsas.online/api/v1/public/branding/logos/logo-1"
import { afterEach, describe, expect, it, vi } from "vitest";

const RELATIVE = "/api/v1/public/branding/logos/logo-1";
const FLIT_API = "https://api.qa.flitsas.online/api/v1";
const EXPECTED_FLIT = "https://api.qa.flitsas.online/api/v1/public/branding/logos/logo-1";

async function loadBranding(base: string | undefined) {
  vi.resetModules();
  if (base === undefined) {
    vi.unstubAllEnvs();
  } else {
    vi.stubEnv("NEXT_PUBLIC_API_BASE_URL", base);
  }
  return import("../branding");
}

const originalFetch = global.fetch;

afterEach(() => {
  vi.unstubAllEnvs();
  vi.resetModules();
  global.fetch = originalFetch;
});

describe("brandLogoUrl — host FLIT (API cross-origin)", () => {
  it("convierte la ruta relativa del backend en URL absoluta contra la API, sin duplicar /api/v1", async () => {
    const { brandLogoUrl } = await loadBranding(FLIT_API);
    const url = brandLogoUrl(RELATIVE);
    expect(url).toBe(EXPECTED_FLIT);
    expect(url).not.toContain("/api/v1/api/v1");
  });

  it("acepta un logoId pelado y arma la ruta pública (patrón bannerImageUrl)", async () => {
    const { brandLogoUrl } = await loadBranding(FLIT_API);
    expect(brandLogoUrl("logo-1")).toBe(EXPECTED_FLIT);
  });

  it("es idempotente: una URL ya absoluta se respeta tal cual (no se duplica ni se re-prefija)", async () => {
    const { brandLogoUrl } = await loadBranding(FLIT_API);
    expect(brandLogoUrl(EXPECTED_FLIT)).toBe(EXPECTED_FLIT);
    expect(brandLogoUrl(brandLogoUrl(RELATIVE))).toBe(EXPECTED_FLIT);
    // Un origen ajeno (p. ej. CDN) tampoco se reescribe.
    expect(brandLogoUrl("https://cdn.example.com/logo.png")).toBe("https://cdn.example.com/logo.png");
  });

  it("null / vacío devuelven null", async () => {
    const { brandLogoUrl } = await loadBranding(FLIT_API);
    expect(brandLogoUrl(null)).toBeNull();
    expect(brandLogoUrl(undefined)).toBeNull();
    expect(brandLogoUrl("   ")).toBeNull();
  });
});

describe("brandLogoUrl — host de red / dev local (sin base configurada)", () => {
  it("cae a window.location.origin (same-origin vía nginx), conservando el comportamiento de #12419", async () => {
    const { brandLogoUrl } = await loadBranding(undefined);
    expect(brandLogoUrl(RELATIVE)).toBe(`${window.location.origin}${RELATIVE}`);
  });
});

describe("clientes de branding — devuelven logoUrl ya absoluta (contrato)", () => {
  function tenantBranding(logoUrl: string | null) {
    return {
      tenantId: "tenant-1",
      draft: { platformName: "Movilidad Andina", colors: null, logoId: "logo-1" },
      published: null,
      publishedVersion: 0,
      publishedAt: null,
      publishedBy: null,
      hasUnpublishedChanges: true,
      logoUrl,
      completeness: { isComplete: false, missing: [] },
      rowVersion: 1,
    };
  }

  it("getBranding (admin, host FLIT) normaliza logoUrl a la URL de la API", async () => {
    const { getBranding } = await loadBranding(FLIT_API);
    global.fetch = vi.fn(async () => new Response(JSON.stringify(tenantBranding(RELATIVE)), { status: 200 })) as never;

    const result = await getBranding("admin", "tenant-1");

    expect(result.logoUrl).toBe(EXPECTED_FLIT);
  });

  it("getBranding conserva logoUrl null cuando aún no hay logotipo", async () => {
    const { getBranding } = await loadBranding(FLIT_API);
    global.fetch = vi.fn(async () => new Response(JSON.stringify(tenantBranding(null)), { status: 200 })) as never;

    const result = await getBranding("company");

    expect(result.logoUrl).toBeNull();
  });

  it("upsertBrandingDraft normaliza logoUrl de la respuesta del PUT", async () => {
    const { upsertBrandingDraft } = await loadBranding(FLIT_API);
    global.fetch = vi.fn(async () => new Response(JSON.stringify(tenantBranding(RELATIVE)), { status: 200 })) as never;

    const result = await upsertBrandingDraft("admin", { logoId: "logo-1", rowVersion: 1 }, "tenant-1");

    expect(result.logoUrl).toBe(EXPECTED_FLIT);
  });

  it("uploadBrandLogo normaliza logoUrl del POST multipart (lo que ve el uploader tras subir)", async () => {
    const { uploadBrandLogo } = await loadBranding(FLIT_API);
    global.fetch = vi.fn(
      async () =>
        new Response(
          JSON.stringify({
            logoId: "logo-1",
            version: 1,
            contentType: "image/png",
            width: 300,
            height: 100,
            sizeBytes: 1024,
            sha256: "abc",
            logoUrl: RELATIVE,
          }),
          { status: 201 },
        ),
    ) as never;

    const result = await uploadBrandLogo("admin", new File(["img"], "logo.png", { type: "image/png" }), "tenant-1");

    expect(result.logoId).toBe("logo-1");
    expect(result.logoUrl).toBe(EXPECTED_FLIT);
  });
});

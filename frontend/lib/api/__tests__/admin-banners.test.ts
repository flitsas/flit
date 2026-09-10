// HU #12241 (Feature #12236) — cliente de banners promocionales admin. Cubre: (1) create/update
// mandan `multipart/form-data` DIRECTO al API (sin presigned storage, a diferencia de
// escrituras/documentos personalizados); (2) el error 422 `{ error }` del backend se traduce a
// `ApiError` con el mensaje legible; (3) `bannerImageUrl` arma la ruta pública SIEMPRE a partir
// del `id`, nunca del campo crudo `imageUrl`; (4) las fechas se normalizan a inicio/fin de día UTC.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../types";
import {
  bannerDateInputValue,
  bannerImageUrl,
  createBanner,
  deleteBanner,
  fetchBanners,
  setBannerActive,
  updateBanner,
  type Banner,
  type BannerFormInput,
} from "../admin-banners";

const originalFetch = global.fetch;

function banner(overrides: Partial<Banner> = {}): Banner {
  return {
    id: "b1",
    name: "Promo verano",
    imageUrl: "/public/banners/b1/image",
    imageSha256: "hash",
    linkUrl: null,
    validFrom: null,
    validUntil: null,
    isActive: true,
    estado: "activo",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: null,
    rowVersion: 1,
    ...overrides,
  };
}

const EMPTY_INPUT: BannerFormInput = {
  name: "Promo verano",
  linkUrl: "",
  validFrom: "",
  validUntil: "",
  file: null,
};

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  global.fetch = originalFetch;
});

describe("bannerImageUrl", () => {
  it("arma la ruta pública SIEMPRE a partir del id, nunca del campo imageUrl crudo", () => {
    expect(bannerImageUrl("b1")).toBe("/api/v1/public/banners/b1/image");
  });
});

describe("bannerDateInputValue", () => {
  it("recorta el ISO a yyyy-mm-dd para precargar <input type=date>", () => {
    expect(bannerDateInputValue("2026-09-15T00:00:00Z")).toBe("2026-09-15");
  });

  it("devuelve cadena vacía si no hay fecha", () => {
    expect(bannerDateInputValue(null)).toBe("");
  });
});

describe("createBanner", () => {
  it("envía multipart/form-data DIRECTO al POST del API (sin presigned storage)", async () => {
    let capturedUrl = "";
    let capturedForm: FormData | null = null;
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedForm = init?.body as FormData;
      return new Response(JSON.stringify(banner()), { status: 201 });
    }) as never;

    const file = new File(["img"], "banner.png", { type: "image/png" });
    const result = await createBanner({
      name: "Promo verano",
      linkUrl: "https://flitsas.com/promo",
      validFrom: "2026-09-01",
      validUntil: "2026-09-30",
      file,
    });

    expect(capturedUrl).toContain("/api/v1/admin/banners");
    expect(result.id).toBe("b1");

    const form = capturedForm as unknown as FormData;
    expect(form.get("name")).toBe("Promo verano");
    expect(form.get("linkUrl")).toBe("https://flitsas.com/promo");
    // Inicio de día / fin de día en UTC (BannerEstadoCalculator compara contra el rango completo).
    expect(form.get("validFrom")).toBe("2026-09-01T00:00:00.000Z");
    expect(form.get("validUntil")).toBe("2026-09-30T23:59:59.999Z");
    expect(form.get("file")).toBe(file);
  });

  it("omite linkUrl/validFrom/validUntil cuando vienen vacíos", async () => {
    let capturedForm: FormData | null = null;
    global.fetch = vi.fn(async (_url: string | URL, init?: RequestInit) => {
      capturedForm = init?.body as FormData;
      return new Response(JSON.stringify(banner()), { status: 201 });
    }) as never;

    await createBanner(EMPTY_INPUT);

    const form = capturedForm as unknown as FormData;
    expect(form.get("linkUrl")).toBeNull();
    expect(form.get("validFrom")).toBeNull();
    expect(form.get("validUntil")).toBeNull();
  });

  it("traduce el 422 { error } del backend a ApiError con el mensaje legible", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: "El nombre del banner es obligatorio." }), { status: 422 }),
    ) as never;

    await expect(createBanner(EMPTY_INPUT)).rejects.toMatchObject({
      message: "El nombre del banner es obligatorio.",
      status: 422,
    });
  });

  it("propaga ApiError en fallos no-2xx genéricos", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 })) as never;
    await expect(createBanner(EMPTY_INPUT)).rejects.toBeInstanceOf(ApiError);
  });
});

describe("updateBanner", () => {
  it("hace PUT a /{id} y permite omitir el archivo (conserva la imagen actual)", async () => {
    let capturedUrl = "";
    let capturedMethod = "";
    let capturedForm: FormData | null = null;
    global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
      capturedUrl = url.toString();
      capturedMethod = init?.method ?? "";
      capturedForm = init?.body as FormData;
      return new Response(JSON.stringify(banner({ id: "b2" })), { status: 200 });
    }) as never;

    const result = await updateBanner("b2", EMPTY_INPUT);

    expect(capturedMethod).toBe("PUT");
    expect(capturedUrl).toContain("/api/v1/admin/banners/b2");
    expect(result.id).toBe("b2");
    expect((capturedForm as unknown as FormData).get("file")).toBeNull();
  });
});

describe("setBannerActive / deleteBanner", () => {
  it("setBannerActive hace PATCH JSON con isActive", async () => {
    let capturedInit: RequestInit | undefined;
    global.fetch = vi.fn(async (_url: string | URL, init?: RequestInit) => {
      capturedInit = init;
      return new Response(null, { status: 204 });
    }) as never;

    await setBannerActive("b1", false);

    expect(capturedInit?.method).toBe("PATCH");
    expect(JSON.parse(capturedInit?.body as string)).toEqual({ isActive: false });
  });

  it("deleteBanner manda confirm=true en query string (AC3)", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(null, { status: 204 });
    }) as never;

    await deleteBanner("b1");

    expect(capturedUrl).toContain("/api/v1/admin/banners/b1");
    expect(capturedUrl).toContain("confirm=true");
  });
});

describe("fetchBanners", () => {
  it("pagina el listado y propaga includeDeleted", async () => {
    let capturedUrl = "";
    global.fetch = vi.fn(async (url: string | URL) => {
      capturedUrl = url.toString();
      return new Response(
        JSON.stringify({ data: [banner()], totalCount: 1, page: 1, pageSize: 20 }),
        { status: 200 },
      );
    }) as never;

    const result = await fetchBanners({ page: 1, pageSize: 20, includeDeleted: true });

    expect(capturedUrl).toContain("page=1");
    expect(capturedUrl).toContain("pageSize=20");
    expect(capturedUrl).toContain("includeDeleted=true");
    expect(result.data).toHaveLength(1);
  });
});

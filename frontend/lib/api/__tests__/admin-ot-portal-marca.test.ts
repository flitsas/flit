// HU #14377 — cliente de la marca del portal del OT: rutas del contrato, alcance por organismo y motivo exacto del error.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  deletePortalMarcaBanner,
  deletePortalMarcaBannerItem,
  reorderPortalMarcaBanners,
  updatePortalMarcaBannerItem,
  uploadPortalMarcaBannerItem,
  deletePortalMarcaCarrusel,
  reorderPortalMarcaCarrusel,
  updatePortalMarcaCarrusel,
  uploadPortalMarcaCarrusel,
  deletePortalMarcaLogo,
  fetchPortalMarcaImage,
  getPortalMarca,
  portalMarcaErrorCode,
  portalMarcaErrorMessage,
  publishPortalMarca,
  retirePortalMarca,
  savePortalMarcaDraft,
  uploadPortalMarcaBanner,
  uploadPortalMarcaLogo,
} from "../admin-ot-portal-marca";
import { ApiError } from "../types";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

describe("admin-ot-portal-marca", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    vi.stubGlobal("fetch", fetchMock);
    window.localStorage.setItem("flit:jwt", "tok-123");
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  const lastCall = () => {
    const [url, init] = fetchMock.mock.calls.at(-1) as [string, RequestInit];
    return { url: new URL(url), init, headers: init.headers as Record<string, string> };
  };

  it("GET: ruta del contrato, token y organismo en la query (SuperAdmin)", async () => {
    fetchMock.mockResolvedValue(json({ transitOfficeId: "ot-1" }));
    await getPortalMarca({ transitOfficeId: "ot-1" });
    const { url, init, headers } = lastCall();
    expect(url.pathname).toBe("/api/v1/admin/ot/portal-marca");
    expect(url.searchParams.get("transitOfficeId")).toBe("ot-1");
    expect(init.method).toBe("GET");
    expect(headers.Authorization).toBe("Bearer tok-123");
  });

  it("sin alcance no manda transitOfficeId (el ot_admin usa el de su token)", async () => {
    fetchMock.mockImplementation(async () => json({}));
    await getPortalMarca();
    expect(lastCall().url.search).toBe("");
  });

  it("PUT, publish y retire llevan el cuerpo del contrato con rowVersion", async () => {
    fetchMock.mockImplementation(async () => json({}));
    const colors = { primary: "#0B3D91", secondary: "#0F766E", onPrimary: "#FFFFFF" };

    await savePortalMarcaDraft({ platformName: "Cali", colors, rowVersion: 2 }, { transitOfficeId: "ot-1" });
    expect(lastCall().init.method).toBe("PUT");
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ platformName: "Cali", colors, rowVersion: 2 });
    expect(lastCall().headers["Content-Type"]).toBe("application/json");

    await publishPortalMarca(3, { transitOfficeId: "ot-1" });
    expect(lastCall().url.pathname).toBe("/api/v1/admin/ot/portal-marca/publish");
    expect(lastCall().init.method).toBe("POST");
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ rowVersion: 3 });

    await retirePortalMarca(4);
    expect(lastCall().url.pathname).toBe("/api/v1/admin/ot/portal-marca/retire");
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ rowVersion: 4 });
  });

  it("carrusel: multipart con alt y enlace, edición, orden y borrado con el mismo ámbito", async () => {
    fetchMock.mockImplementation(async () => json({}));
    const file = new File(["x"], "a.png", { type: "image/png" });
    const scope = { transitOfficeId: "ot-1" };

    await uploadPortalMarcaCarrusel(file, "Promo", "https://x.co", scope);
    const form = lastCall().init.body as FormData;
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["POST", "/api/v1/admin/ot/portal-marca/carrusel"]);
    expect([form.get("file"), form.get("alt"), form.get("linkUrl")]).toEqual([file, "Promo", "https://x.co"]);
    expect(lastCall().url.searchParams.get("transitOfficeId")).toBe("ot-1");

    await uploadPortalMarcaCarrusel(file, "Sin enlace", null);
    expect((lastCall().init.body as FormData).has("linkUrl")).toBe(false);

    await updatePortalMarcaCarrusel("img-1", { alt: "N", linkUrl: null }, scope);
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["PUT", "/api/v1/admin/ot/portal-marca/carrusel/img-1"]);
    expect(lastCall().init.body).toBe(JSON.stringify({ alt: "N", linkUrl: null }));

    await reorderPortalMarcaCarrusel(["b", "a"], scope);
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["PUT", "/api/v1/admin/ot/portal-marca/carrusel/orden"]);
    expect(lastCall().init.body).toBe(JSON.stringify({ imageIds: ["b", "a"] }));

    await deletePortalMarcaCarrusel("img-1", scope);
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["DELETE", "/api/v1/admin/ot/portal-marca/carrusel/img-1"]);
  });

  it("banners: multipart con alt y enlace, edición, orden y borrado en /banners con el mismo ámbito", async () => {
    fetchMock.mockImplementation(async () => json({}));
    const file = new File(["x"], "b.png", { type: "image/png" });
    const scope = { transitOfficeId: "ot-1" };

    await uploadPortalMarcaBannerItem(file, "Bienvenida", "https://x.co", scope);
    const form = lastCall().init.body as FormData;
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["POST", "/api/v1/admin/ot/portal-marca/banners"]);
    expect([form.get("file"), form.get("alt"), form.get("linkUrl")]).toEqual([file, "Bienvenida", "https://x.co"]);
    expect(lastCall().url.searchParams.get("transitOfficeId")).toBe("ot-1");
    expect(lastCall().headers["Content-Type"]).toBeUndefined();

    await uploadPortalMarcaBannerItem(file, "Sin enlace", null);
    expect((lastCall().init.body as FormData).has("linkUrl")).toBe(false);

    await updatePortalMarcaBannerItem("ban-1", { alt: "N", linkUrl: null }, scope);
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["PUT", "/api/v1/admin/ot/portal-marca/banners/ban-1"]);
    expect(lastCall().init.body).toBe(JSON.stringify({ alt: "N", linkUrl: null }));

    await reorderPortalMarcaBanners(["b", "a"], scope);
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["PUT", "/api/v1/admin/ot/portal-marca/banners/orden"]);
    expect(lastCall().init.body).toBe(JSON.stringify({ imageIds: ["b", "a"] }));

    await deletePortalMarcaBannerItem("ban-1", scope);
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["DELETE", "/api/v1/admin/ot/portal-marca/banners/ban-1"]);
  });

  it("los errores de banners conservan el código y el motivo (BANNERS_LLENO, 404, 403)", async () => {
    fetchMock.mockResolvedValueOnce(json({ code: "BANNERS_LLENO", message: "Ya hay 6 banners." }, 409));
    const lleno = await uploadPortalMarcaBannerItem(new File(["x"], "b.png"), "A", null).catch((e: unknown) => e);
    expect([portalMarcaErrorCode(lleno), portalMarcaErrorMessage(lleno)]).toEqual(["BANNERS_LLENO", "Ya hay 6 banners."]);
    fetchMock.mockResolvedValueOnce(json({ code: "PORTAL_MARCA_NO_ENCONTRADA", message: "El banner no existe." }, 404));
    expect(portalMarcaErrorCode(await deletePortalMarcaBannerItem("x").catch((e: unknown) => e))).toBe("PORTAL_MARCA_NO_ENCONTRADA");
  });

  it("logo y banner único (legado): multipart `file` (sin Content-Type manual) y DELETE", async () => {
    fetchMock.mockImplementation(async () => json({}));
    const file = new File(["x"], "logo.png", { type: "image/png" });

    await uploadPortalMarcaLogo(file, { transitOfficeId: "ot-1" });
    expect(lastCall().url.pathname).toBe("/api/v1/admin/ot/portal-marca/logo");
    expect(lastCall().init.body).toBeInstanceOf(FormData);
    expect((lastCall().init.body as FormData).get("file")).toBe(file);
    expect(lastCall().headers["Content-Type"]).toBeUndefined();

    await uploadPortalMarcaBanner(file);
    expect(lastCall().url.pathname).toBe("/api/v1/admin/ot/portal-marca/banner");

    await deletePortalMarcaLogo();
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["DELETE", "/api/v1/admin/ot/portal-marca/logo"]);
    await deletePortalMarcaBanner();
    expect([lastCall().init.method, lastCall().url.pathname]).toEqual(["DELETE", "/api/v1/admin/ot/portal-marca/banner"]);
  });

  it("errores {code, message}: conserva el motivo exacto, también en 422 y 409", async () => {
    fetchMock.mockResolvedValue(json({ code: "PORTAL_MARCA_CONTRASTE", message: "El contraste es 3,1:1 y debe ser al menos 4,5:1." }, 422));
    const error = await publishPortalMarca(1).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(422);
    expect(portalMarcaErrorMessage(error)).toBe("El contraste es 3,1:1 y debe ser al menos 4,5:1.");
    expect(portalMarcaErrorCode(error)).toBe("PORTAL_MARCA_CONTRASTE");

    fetchMock.mockResolvedValue(json({ code: "PORTAL_MARCA_CONFLICTO", message: "Otra persona cambió la marca." }, 409));
    expect(portalMarcaErrorCode(await retirePortalMarca(1).catch((e: unknown) => e))).toBe("PORTAL_MARCA_CONFLICTO");
  });

  it("403 de otro organismo: su motivo; sin cuerpo: un texto genérico que no filtra la ruta", async () => {
    fetchMock.mockResolvedValueOnce(json({ code: "FORBIDDEN", message: "No puedes ver la marca de otro organismo." }, 403));
    expect(portalMarcaErrorMessage(await getPortalMarca().catch((e: unknown) => e))).toBe("No puedes ver la marca de otro organismo.");

    fetchMock.mockResolvedValueOnce(new Response("<html>", { status: 500 }));
    const message = portalMarcaErrorMessage(await getPortalMarca().catch((e: unknown) => e));
    expect(message).toBe("No se pudo completar la solicitud. Inténtalo de nuevo.");
    expect(message).not.toMatch(/500|api\/v1/);
  });

  it("sin red: un mensaje claro", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    expect(portalMarcaErrorMessage(await getPortalMarca().catch((e: unknown) => e))).toMatch(/No se pudo conectar/);
  });

  it("la imagen del borrador se pide con el token y se entrega como URL de objeto", async () => {
    fetchMock.mockResolvedValue(new Response(new Blob(["img"], { type: "image/png" }), { status: 200 }));
    const createObjectURL = vi.fn(() => "blob:abc");
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL }));

    const src = await fetchPortalMarcaImage("/api/v1/admin/ot/portal-marca/imagenes/img-1", { transitOfficeId: "ot-1" });

    expect(src).toBe("blob:abc");
    expect(lastCall().url.pathname).toBe("/api/v1/admin/ot/portal-marca/imagenes/img-1");
    expect(lastCall().url.searchParams.get("transitOfficeId")).toBe("ot-1");
    expect(lastCall().headers.Authorization).toBe("Bearer tok-123");
  });
});

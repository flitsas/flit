import { describe, expect, it, vi } from "vitest";
import { isTramitesPublicApiPath } from "@/lib/auth/bff-public-path";

// HU #14546 — captura manual pública vía BFF OIDC sin sesión.

describe("isTramitesPublicApiPath (AC2 / AC4)", () => {
  it("AC2 — manual-capture y el resto de /public/* son anónimos", () => {
    expect(isTramitesPublicApiPath(["public", "manual-capture", "token"])).toBe(true);
    expect(isTramitesPublicApiPath(["public", "biometric", "token"])).toBe(true);
    expect(isTramitesPublicApiPath(["public", "portal", "slug"])).toBe(true);
    expect(isTramitesPublicApiPath(["public", "banners", "active"])).toBe(true);
  });

  it("AC4 — rutas autenticadas no son anónimas", () => {
    expect(isTramitesPublicApiPath(["tramites", "instances"])).toBe(false);
    expect(isTramitesPublicApiPath(["security", "users"])).toBe(false);
    expect(isTramitesPublicApiPath([])).toBe(false);
  });
});

describe("BFF route elige proxy según path (AC2 / AC4)", () => {
  it("public → tramitesPublicApiProxy; resto → tramitesApiProxy", async () => {
    const publicProxy = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const authedProxy = vi.fn(async () =>
      Response.json({ code: "SESSION_EXPIRED", message: "La sesión venció." }, { status: 401 }),
    );

    vi.resetModules();
    vi.doMock("@flit/auth/request", () => ({ isOidcSessionMode: () => true }));
    vi.doMock("@/lib/auth/oidc.server", () => ({
      tramitesPublicApiProxy: publicProxy,
      tramitesApiProxy: authedProxy,
    }));

    const { GET } = await import("@/app/bff/api/v1/[...path]/route");

    const publicReq = new Request("https://tramites.flitsas.com/bff/api/v1/public/manual-capture/tok");
    const publicRes = await GET(publicReq, { params: Promise.resolve({ path: ["public", "manual-capture", "tok"] }) });
    expect(publicRes.status).toBe(200);
    expect(publicProxy).toHaveBeenCalledTimes(1);
    expect(authedProxy).not.toHaveBeenCalled();

    const authedReq = new Request("https://tramites.flitsas.com/bff/api/v1/tramites/instances");
    const authedRes = await GET(authedReq, { params: Promise.resolve({ path: ["tramites", "instances"] }) });
    expect(authedRes.status).toBe(401);
    expect(authedProxy).toHaveBeenCalledTimes(1);
    await expect(authedRes.json()).resolves.toMatchObject({ code: "SESSION_EXPIRED" });
  });
});

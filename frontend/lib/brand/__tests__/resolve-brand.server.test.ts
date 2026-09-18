import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * `next/headers` y `server-only` se sustituyen porque ninguno existe fuera del runtime de Next
 * (mismo patrón que `lib/migracion/__tests__/server-guard.test.ts`).
 *
 * Uso de ejemplo:
 *   host = "flitsas.online" → resolveBrand() devuelve FLIT_BRAND, SIN llamar a fetch (AC7).
 *   host = "app.red.com"    → resolveBrand() hace GET /public/branding con X-Flit-Domain.
 */
const state = { host: "localhost:3000" as string | null };

vi.mock("server-only", () => ({}));
vi.mock("next/headers", () => ({
  headers: () =>
    Promise.resolve({
      get: (name: string) => (name === "host" ? state.host : null),
    }),
}));

describe("resolveBrand — HU #12419 AC1/AC5/AC7", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.unstubAllEnvs();
    state.host = "localhost:3000";
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it("host FLIT (AC7): devuelve FLIT_BRAND SIN llamar a fetch — cero llamadas nuevas", async () => {
    state.host = "dev.flitsas.online";
    const { resolveBrand } = await import("../resolve-brand.server");
    const { FLIT_BRAND } = await import("../types");

    const brand = await resolveBrand();

    expect(brand).toEqual(FLIT_BRAND);
    expect(fetch).not.toHaveBeenCalled();
  });

  it("host de red con respuesta 200 válida: devuelve la marca resuelta y envía X-Flit-Domain/X-Internal-Key", async () => {
    state.host = "app.movilidadandina.com";
    vi.stubEnv("FLIT_INTERNAL_API_KEY", "clave-interna-test");
    vi.stubEnv("BRANDING_INTERNAL_API_URL", "http://gateway:4002");

    const networkBrand = {
      platformName: "Movilidad Andina",
      logoUrl: "/api/v1/public/branding/logos/019254a0-7c3e-7b5a-9f1e-3a2b4c5d6e7f",
      colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
      version: 3,
    };
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(networkBrand), { status: 200, headers: { "content-type": "application/json" } }),
    );

    const { resolveBrand } = await import("../resolve-brand.server");
    const brand = await resolveBrand();

    expect(brand).toEqual(networkBrand);
    expect(fetch).toHaveBeenCalledWith(
      "http://gateway:4002/api/v1/public/branding",
      expect.objectContaining({
        headers: expect.objectContaining({
          "X-Flit-Domain": "app.movilidadandina.com",
          "X-Internal-Key": "clave-interna-test",
        }),
      }),
    );
  });

  it("host de red SIN FLIT_INTERNAL_API_KEY: no manda X-Internal-Key (respaldo sigue siendo FLIT si el Gateway la exige)", async () => {
    state.host = "app.movilidadandina.com";
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        JSON.stringify({ platformName: "X", logoUrl: null, colors: { primary: "#000000", secondary: "#000000", onPrimary: "#FFFFFF" }, version: 1 }),
        { status: 200 },
      ),
    );

    const { resolveBrand } = await import("../resolve-brand.server");
    await resolveBrand();

    const [, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>)["X-Internal-Key"]).toBeUndefined();
  });

  it("fallo de red/timeout: cae a FLIT_BRAND sin lanzar (AC5)", async () => {
    state.host = "app.movilidadandina.com";
    vi.mocked(fetch).mockRejectedValue(new DOMException("timeout", "TimeoutError"));

    const { resolveBrand } = await import("../resolve-brand.server");
    const { FLIT_BRAND } = await import("../types");

    await expect(resolveBrand()).resolves.toEqual(FLIT_BRAND);
  });

  it("respuesta no-200: cae a FLIT_BRAND (AC5)", async () => {
    state.host = "app.movilidadandina.com";
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 500 }));

    const { resolveBrand } = await import("../resolve-brand.server");
    const { FLIT_BRAND } = await import("../types");

    await expect(resolveBrand()).resolves.toEqual(FLIT_BRAND);
  });

  it("forma de respuesta inválida (contrato roto): cae a FLIT_BRAND (AC5)", async () => {
    state.host = "app.movilidadandina.com";
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ nombre: "algo-que-no-cumple-el-contrato" }), { status: 200 }),
    );

    const { resolveBrand } = await import("../resolve-brand.server");
    const { FLIT_BRAND } = await import("../types");

    await expect(resolveBrand()).resolves.toEqual(FLIT_BRAND);
  });

  it("host desconocido (null): se trata como FLIT (respaldo seguro)", async () => {
    state.host = null;
    const { resolveBrand } = await import("../resolve-brand.server");
    const { FLIT_BRAND } = await import("../types");

    await expect(resolveBrand()).resolves.toEqual(FLIT_BRAND);
    expect(fetch).not.toHaveBeenCalled();
  });
});

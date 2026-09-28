import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// B-09 — el hub define FLIT_HOSTS en runtime (no hay NEXT_PUBLIC_* horneado): la misma imagen decide en cada
// ambiente qué hosts son FLIT y cuáles son dominios de red.
vi.mock("server-only", () => ({}));
const host = vi.hoisted(() => ({ value: "" }));
vi.mock("next/headers", () => ({ headers: async () => new Headers({ host: host.value }) }));

describe("resolveBrand con FLIT_HOSTS", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 500 })));
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });

  it("un host de la lista es FLIT y no llama al gateway", async () => {
    vi.stubEnv("FLIT_HOSTS", "hub.interno.test");
    host.value = "hub.interno.test";
    const { resolveBrand } = await import("@flit/brand/resolve-brand.server");

    const brand = await resolveBrand();

    expect(brand.version).toBe(0);
    expect(fetch).not.toHaveBeenCalled();
  });

  it("un host fuera de la lista se trata como dominio de red y pide su marca", async () => {
    vi.stubEnv("FLIT_HOSTS", "*.flitsas.online,!marcablancadev.flitsas.online");
    host.value = "marcablancadev.flitsas.online";
    const { resolveBrand } = await import("@flit/brand/resolve-brand.server");

    await resolveBrand();

    const [, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect((init.headers as Record<string, string>)["X-Flit-Domain"]).toBe("marcablancadev.flitsas.online");
  });
});

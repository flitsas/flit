import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * Uso de ejemplo:
 *   resolveApiBase("https://api.dev.flitsas.online/api/v1") en host FLIT → la misma base
 *   resolveApiBase("https://api.dev.flitsas.online/api/v1") en host de red → "" (same-origin)
 */
describe("resolveApiBase — HU #12419", () => {
  const originalLocation = window.location;

  function setHost(host: string) {
    // jsdom permite reasignar `window.location` completo (no solo `href`) redefiniendo la
    // propiedad — patrón habitual para simular un host distinto sin navegar de verdad.
    Object.defineProperty(window, "location", {
      value: { ...originalLocation, host, hostname: host.split(":")[0] },
      writable: true,
      configurable: true,
    });
  }

  beforeEach(() => {
    vi.unstubAllEnvs();
  });

  afterEach(() => {
    Object.defineProperty(window, "location", { value: originalLocation, writable: true, configurable: true });
    vi.unstubAllEnvs();
  });

  it("host FLIT: devuelve la base configurada tal cual (paridad AC7)", async () => {
    setHost("dev.flitsas.online");
    const { resolveApiBase } = await import("../base-url");

    expect(resolveApiBase("https://api.dev.flitsas.online/api/v1")).toBe("https://api.dev.flitsas.online/api/v1");
  });

  it("host de red: devuelve '' — el llamante cae a window.location.origin (same-origin, #12421)", async () => {
    setHost("app.movilidadandina.com");
    const { resolveApiBase } = await import("../base-url");

    expect(resolveApiBase("https://api.dev.flitsas.online/api/v1")).toBe("");
  });

  it("edge case: base configurada vacía (dev local) en host FLIT sigue vacía", async () => {
    setHost("localhost:3000");
    const { resolveApiBase } = await import("../base-url");

    expect(resolveApiBase("")).toBe("");
  });

  it("respeta NEXT_PUBLIC_FLIT_HOSTS al decidir si el host es de red", async () => {
    vi.stubEnv("NEXT_PUBLIC_FLIT_HOSTS", "miplataforma.com");
    setHost("miplataforma.com");
    const { resolveApiBase } = await import("../base-url");

    expect(resolveApiBase("https://api.example.com")).toBe("https://api.example.com");
  });
});

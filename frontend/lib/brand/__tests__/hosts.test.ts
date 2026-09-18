import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * Uso de ejemplo: `isFlitHost("app.movilidadandina.com")` → `false` con la lista por defecto;
 * `isFlitHost("dev.flitsas.online")` → `true` (matchea `*.flitsas.online`).
 */
async function loadIsFlitHost(envValue?: string) {
  vi.resetModules();
  if (envValue === undefined) {
    vi.unstubAllEnvs();
  } else {
    vi.stubEnv("NEXT_PUBLIC_FLIT_HOSTS", envValue);
  }
  const mod = await import("../hosts");
  return mod.isFlitHost;
}

describe("isFlitHost — AC1/AC7 #12419", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("sin NEXT_PUBLIC_FLIT_HOSTS, usa el respaldo por defecto (localhost)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("localhost:3000")).toBe(true);
    expect(isFlitHost("127.0.0.1:3000")).toBe(true);
  });

  it("respaldo por defecto matchea *.flitsas.online y *.flitsas.com", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("dev.flitsas.online")).toBe(true);
    expect(isFlitHost("api.pdn.flitsas.com")).toBe(true);
  });

  it("un dominio de red (fuera de la lista) NO es FLIT (edge case)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("app.movilidadandina.com")).toBe(false);
  });

  it("respeta NEXT_PUBLIC_FLIT_HOSTS explícita (contrato)", async () => {
    const isFlitHost = await loadIsFlitHost("miplataforma.com,*.miplataforma.com");
    expect(isFlitHost("miplataforma.com")).toBe(true);
    expect(isFlitHost("app.miplataforma.com")).toBe(true);
    expect(isFlitHost("flitsas.online")).toBe(false);
  });

  it("ignora mayúsculas y el puerto del host header", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("LOCALHOST:4001")).toBe(true);
  });

  it("host null/vacío se trata como FLIT (respaldo seguro, AC5)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost(null)).toBe(true);
    expect(isFlitHost("")).toBe(true);
  });

  it("*.dominio NO matchea el dominio raíz sin subdominio (contrato)", async () => {
    const isFlitHost = await loadIsFlitHost("*.flitsas.online");
    expect(isFlitHost("flitsas.online")).toBe(false);
    expect(isFlitHost("dev.flitsas.online")).toBe(true);
  });
});

import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * HU #14770 — flag de montaje de la UI de DR. FLIT.
 * Se reimporta el módulo tras stubEnv porque la constante se evalúa al cargar.
 */
describe("DR_FLIT_UI_ENABLED (HU #14770)", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("AC2 — apagado por defecto (sin env o valor distinto de true)", async () => {
    vi.stubEnv("NEXT_PUBLIC_DR_FLIT_UI_ENABLED", undefined);
    vi.resetModules();
    const { DR_FLIT_UI_ENABLED } = await import("../dr-flit-intents");
    expect(DR_FLIT_UI_ENABLED).toBe(false);

    vi.stubEnv("NEXT_PUBLIC_DR_FLIT_UI_ENABLED", "false");
    vi.resetModules();
    const off = await import("../dr-flit-intents");
    expect(off.DR_FLIT_UI_ENABLED).toBe(false);
  });

  it("AC2 — solo con 'true' se considera encendido", async () => {
    vi.stubEnv("NEXT_PUBLIC_DR_FLIT_UI_ENABLED", "true");
    vi.resetModules();
    const { DR_FLIT_UI_ENABLED } = await import("../dr-flit-intents");
    expect(DR_FLIT_UI_ENABLED).toBe(true);
  });
});

import { beforeEach, describe, expect, it, vi } from "vitest";

/**
 * HU #12419 AC7 — paridad exacta en host FLIT: título de la pestaña, icono y sin `<style
 * id="brand-tokens">`. Se prueba `generateMetadata()` de `app/layout.tsx` directamente (sin
 * `pnpm build`/Playwright — no hay Playwright configurado en el repo, se documenta en el informe
 * de dev-tester) mockeando `next/headers` igual que `resolve-brand.server.test.ts`.
 */
const state = { host: "localhost:3000" as string | null };

vi.mock("server-only", () => ({}));
vi.mock("next/headers", () => ({
  headers: () => Promise.resolve({ get: (name: string) => (name === "host" ? state.host : null) }),
}));
// next/font/google exige el compilador SWC de Next (webpack/turbopack) para resolver la
// tipografía; fuera de ese runtime (vitest) no es invocable. Se sustituye por un stub — el
// objetivo de este test es `generateMetadata()`, no la carga de fuentes.
vi.mock("next/font/google", () => ({
  Poppins: () => ({ variable: "--font-poppins" }),
  JetBrains_Mono: () => ({ variable: "--font-jetbrains-mono" }),
}));

describe("app/layout · generateMetadata — paridad AC7", () => {
  beforeEach(() => {
    vi.resetModules();
    state.host = "localhost:3000";
    vi.stubGlobal("fetch", vi.fn());
  });

  it("host FLIT: title es exactamente 'FLIT 2.0', sin icons, y SIN llamar a fetch", async () => {
    state.host = "dev.flitsas.online";
    const { generateMetadata } = await import("../layout");

    const metadata = await generateMetadata();

    expect(metadata.title).toBe("FLIT 2.0");
    expect(metadata.description).toBe("Plataforma de trámites vehiculares");
    expect(metadata.icons).toBeUndefined();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("host de red con marca publicada: title y icons reflejan la marca resuelta", async () => {
    state.host = "app.movilidadandina.com";
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          platformName: "Movilidad Andina",
          logoUrl: "/api/v1/public/branding/logos/abc",
          colors: { primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" },
          version: 3,
        }),
        { status: 200 },
      ),
    );

    const { generateMetadata } = await import("../layout");
    const metadata = await generateMetadata();

    expect(metadata.title).toBe("Movilidad Andina");
    expect(metadata.icons).toEqual({ icon: "/api/v1/public/branding/logos/abc" });
  });
});

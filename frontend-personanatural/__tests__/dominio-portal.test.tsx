import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToString } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { marcaAplicable } from "@/lib/marca/marca";

// HU #14386: la raíz de un dominio activo pinta el punto de su organismo. El BFF se simula; no hay dominio real.
const resolverDominioPortal = vi.fn();
const obtenerMarcaPunto = vi.fn();
let host: string | null = "tramites.cali.gov.co";
vi.mock("@/components/marca/MarcaWeb", () => ({ MarcaWeb: ({ children }: { children: React.ReactNode }) => children }));
vi.mock("@/lib/punto/dominio.server", () => ({
  resolverDominioPortal: (...a: unknown[]) => resolverDominioPortal(...a),
}));
vi.mock("@/lib/marca/marca.server", () => ({
  obtenerMarcaPunto: (...a: unknown[]) => obtenerMarcaPunto(...a),
}));
vi.mock("next/headers", () => ({
  headers: async () => new Headers({ ...(host ? { host } : {}), "x-forwarded-for": "190.1.2.3" }),
}));
vi.mock("@/components/landing/Landing", () => ({
  Landing: () => <main data-testid="landing">Landing de persona</main>,
}));
vi.mock("@/components/punto/CanalPunto", () => ({
  CanalPunto: ({ codigo }: { codigo: string }) => (
    <main data-testid="punto">{`Punto ${codigo}`}</main>
  ),
}));

const MARCA = marcaAplicable({
  origen: "organismo",
  nombre: "Tránsito de Cali",
  colores: { primario: "#0B3D91", secundario: "#3D63DD", sobrePrimario: "#FFFFFF" },
  logoUrl: null,
  bannerUrl: null,
  version: 1,
});

async function raizHtml() {
  const pagina = await import("@/app/page");
  const q = new QueryClient();
  return renderToString(
    <QueryClientProvider client={q}>{await pagina.default()}</QueryClientProvider>,
  );
}

beforeEach(() => {
  resolverDominioPortal.mockReset();
  obtenerMarcaPunto.mockReset().mockResolvedValue(MARCA);
  host = "tramites.cali.gov.co";
});

describe("raíz de un dominio de organismo (HU #14386)", () => {
  it("con dominio activo pinta el punto de ese organismo con su marca, sin escribir código", async () => {
    resolverDominioPortal.mockResolvedValue("CALI");
    const html = await raizHtml();
    expect(html).toContain("Punto CALI");
    expect(html).toContain("--primary:#0B3D91");
    expect(resolverDominioPortal.mock.calls[0]![0]).toBe("tramites.cali.gov.co");
    expect(obtenerMarcaPunto.mock.calls[0]![0]).toBe("CALI");
  });

  it("un host que no resuelve deja la web de persona como hoy, sin marca", async () => {
    resolverDominioPortal.mockResolvedValue(null);
    const html = await raizHtml();
    expect(html).toContain("Landing de persona");
    expect(obtenerMarcaPunto).not.toHaveBeenCalled();
  });

  it("la raíz se resuelve en cada petición: sin revalidate ni caché por tiempo", async () => {
    const pagina = await import("@/app/page");
    expect(pagina.dynamic).toBe("force-dynamic");
    expect(pagina).not.toHaveProperty("revalidate");
  });

  it("/o/{codigo} sigue funcionando con el dominio activo", async () => {
    resolverDominioPortal.mockResolvedValue("CALI");
    const { default: Pagina } = await import("@/app/o/[codigo]/page");
    const html = renderToString(await Pagina({ params: Promise.resolve({ codigo: "PASTO" }) }));
    expect(html).toContain("Punto PASTO");
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { readFileSync } from "node:fs";
import { renderToString } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { marcaAplicable } from "@/lib/marca/marca";

// Las páginas leen la marca en el servidor: aquí se simula lo que responde el BFF.
const obtenerMarcaWeb = vi.fn();
const obtenerMarcaPunto = vi.fn();
vi.mock("@/lib/marca/marca.server", () => ({
  obtenerMarcaWeb: (...args: unknown[]) => obtenerMarcaWeb(...args),
  obtenerMarcaPunto: (...args: unknown[]) => obtenerMarcaPunto(...args),
}));
vi.mock("next/headers", () => ({ headers: async () => new Headers({ "x-forwarded-for": "190.1.2.3" }) }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }), usePathname: () => "/" }));

const WEB = {
  origen: "organismo",
  nombre: "Portal Ciudadano",
  colores: { primario: "#0B3D91", secundario: "#3D63DD", sobrePrimario: "#FFFFFF" },
  logoUrl: "/api/v1/persona-natural/marca/imagenes/logo-web",
  bannerUrl: null,
  version: 3,
};
const PUNTO = { ...WEB, nombre: "Tránsito de Cali", colores: { primario: "#8A1C1C", secundario: "#B33A3A", sobrePrimario: "#FFFFFF" }, logoUrl: "/api/v1/persona-natural/marca/imagenes/logo-cali" };

const conQuery = (ui: React.ReactElement) => (
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{ui}</QueryClientProvider>
);

async function raizHtml() {
  const { default: Inicio } = await import("@/app/page");
  // MarcaWeb es un componente de servidor asíncrono: se resuelve a mano, como hace Next.
  const raiz = Inicio() as React.ReactElement<{ children: React.ReactNode }>;
  const { MarcaWeb } = await import("@/components/marca/MarcaWeb");
  return renderToString(conQuery(await MarcaWeb(raiz.props)));
}
async function puntoHtml(codigo = "CALI") {
  const { default: Pagina } = await import("@/app/o/[codigo]/page");
  return renderToString(conQuery(await Pagina({ params: Promise.resolve({ codigo }) })));
}

beforeEach(() => {
  obtenerMarcaWeb.mockReset();
  obtenerMarcaPunto.mockReset();
});

describe("HU #14403 AC1: primera pintura con la marca de la web", () => {
  it("el HTML del servidor ya trae los tokens, el logo y el nombre, antes del contenido", async () => {
    obtenerMarcaWeb.mockResolvedValue(marcaAplicable(WEB));

    const html = await raizHtml();

    expect(html).toContain("--primary:#0B3D91");
    expect(html).toContain("--dashboard-action-background:#3D63DD");
    expect(html).toContain('src="/api/v1/persona-natural/marca/imagenes/logo-web"');
    expect(html).toContain("Portal Ciudadano");
    expect(html.indexOf("--primary:#0B3D91")).toBeLessThan(html.indexOf("<header"));
    expect(obtenerMarcaWeb).toHaveBeenCalledWith(expect.objectContaining({ ipCliente: "190.1.2.3" }));
  });

  it("el título y el botón sobre la tarjeta de acción usan colores que se leen sobre el fondo de la marca", async () => {
    obtenerMarcaWeb.mockResolvedValue(marcaAplicable(WEB));
    const html = await raizHtml();
    expect(html).toContain(".dashboard-institutional-card .dashboard-title{color:var(--primary-foreground)}");
    expect(html).toContain(".dashboard-institutional-card .dashboard-submit{color:var(--marca-texto-boton)}");
  });
});

describe("HU #14403 AC2: sin marca o con falla la web queda como hoy", () => {
  it.each([["sin marca publicada", null]])("%s: sin hoja de estilos y con el logo FLIT", async (_n, valor) => {
    obtenerMarcaWeb.mockResolvedValue(valor);

    const html = await raizHtml();

    expect(html).not.toContain("data-marca-punto");
    expect(html).not.toContain("--primary:");
    expect(html).toContain('src="/assets/flit/logo-flit.svg"');
    expect(html).toContain('alt="FLIT 2.0"');
    expect(html).toContain('aria-label="FLIT 2.0 — Ir al inicio"');
    expect(html).not.toContain("marca/imagenes");
  });

  it("origen flit, colores a medias o imagen ajena no se aplican", () => {
    expect(marcaAplicable({ ...WEB, origen: "flit" }, ["organismo", "web"])).toBeNull();
    expect(marcaAplicable({ ...WEB, colores: { ...WEB.colores, primario: "rojo" } }, ["organismo", "web"])).toBeNull();
    expect(marcaAplicable({ ...WEB, logoUrl: "https://evil.example/x.png" }, ["organismo", "web"])?.logoUrl).toBeNull();
  });
});

describe("HU #14403: la marca de la web y la del organismo no se cruzan", () => {
  it("con marca web publicada, /o/{codigo} no la lee ni la lleva", async () => {
    obtenerMarcaWeb.mockResolvedValue(marcaAplicable(WEB));
    obtenerMarcaPunto.mockResolvedValue(null);

    const html = await puntoHtml();

    expect(obtenerMarcaWeb).not.toHaveBeenCalled();
    expect(html).not.toContain("--primary:#0B3D91");
    expect(html).not.toContain("logo-web");
    expect(html).not.toContain("Portal Ciudadano");
  });

  it("con marca de organismo publicada, la raíz de la web no la lee ni la lleva", async () => {
    obtenerMarcaPunto.mockResolvedValue(marcaAplicable(PUNTO));
    obtenerMarcaWeb.mockResolvedValue(null);

    const html = await raizHtml();

    expect(obtenerMarcaPunto).not.toHaveBeenCalled();
    expect(html).not.toContain("--primary:#8A1C1C");
    expect(html).not.toContain("logo-cali");
    expect(html).toContain('src="/assets/flit/logo-flit.svg"');
  });

  it("cada una pinta la suya cuando las dos están publicadas", async () => {
    obtenerMarcaWeb.mockResolvedValue(marcaAplicable(WEB));
    obtenerMarcaPunto.mockResolvedValue(marcaAplicable(PUNTO));

    const web = await raizHtml();
    const punto = await puntoHtml();

    expect(web).toContain("--primary:#0B3D91");
    expect(web).not.toContain("#8A1C1C");
    expect(punto).toContain("--primary:#8A1C1C");
    expect(punto).not.toContain("#0B3D91");
  });

  it("el layout raíz no lee marca alguna (ni de web ni de organismo)", () => {
    expect(readFileSync("app/layout.tsx", "utf8")).not.toMatch(/EstiloMarca|marca\.server|ProveedorMarca|MarcaWeb/);
  });

  it("las páginas de la web se leen en cada petición, sin caché", async () => {
    expect((await import("@/app/page")).dynamic).toBe("force-dynamic");
    expect((await import("@/app/tramite/[tipo]/page")).dynamic).toBe("force-dynamic");
  });
});

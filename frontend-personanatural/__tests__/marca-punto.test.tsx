import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { renderToString } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProveedorMarca } from "@/components/marca/MarcaPunto";
import { CanalPunto } from "@/components/punto/CanalPunto";
import { crearCliente } from "@/lib/api/cliente";
import { marcaAplicable } from "@/lib/marca/marca";
import { json } from "./utilidades";

// La página lee la marca en el servidor: aquí se simula lo que responde el BFF.
const obtenerMarcaPunto = vi.fn();
vi.mock("@/lib/marca/marca.server", () => ({ obtenerMarcaPunto: (...args: unknown[]) => obtenerMarcaPunto(...args) }));
vi.mock("next/headers", () => ({ headers: async () => new Headers({ "x-forwarded-for": "190.1.2.3" }) }));

const BFF = {
  origen: "organismo",
  nombre: "Tránsito de Cali",
  colores: { primario: "#0B3D91", secundario: "#3D63DD", sobrePrimario: "#FFFFFF" },
  logoUrl: "/api/v1/persona-natural/marca/imagenes/logo-7",
  bannerUrl: "/api/v1/persona-natural/marca/imagenes/banner-9",
  version: 4,
};
const MARCA = marcaAplicable(BFF);

const ENTRADA = { organismoId: "o-1", codigo: "CALI", nombre: "Tránsito de Cali" };
const TIPOS = [{ code: "TRASPASO_STANDARD", name: "Traspaso", family: "TRASPASO", version: 1, disponible: true }];

function clienteFalso() {
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    const rutas: Record<string, () => Response> = {
      "POST /punto/entrada": () => json(ENTRADA),
      "GET /punto/tipos-tramite": () => json(TIPOS),
      "GET /tipos-tramite": () => json(TIPOS),
      "POST /punto/arranque": () => json({ puedeSeguir: true }),
      "POST /punto/diagnostico": () => json({ puedeContinuar: true, requisitos: [] }),
      "POST /punto/consultas/vehiculo": () => json({ existe: true, placa: "ABC123", estado: "ACTIVO" }),
    };
    return (rutas[clave] ?? (() => json({ code: "no_simulado" }, 404)))();
  }) as unknown as typeof fetch;
  return crearCliente({ fetchImpl });
}

const conQuery = (ui: React.ReactElement) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{ui}</QueryClientProvider>;
};

async function paginaHtml(codigo = "CALI") {
  const { default: Pagina } = await import("@/app/o/[codigo]/page");
  return renderToString(conQuery(await Pagina({ params: Promise.resolve({ codigo }) })));
}

beforeEach(() => {
  obtenerMarcaPunto.mockReset();
});

describe("HU #14379 AC1: primera pintura con la marca del organismo", () => {
  it("el HTML del servidor ya trae los tokens, el logo, el nombre y el banner", async () => {
    obtenerMarcaPunto.mockResolvedValue(MARCA);

    const html = await paginaHtml();

    expect(html).toContain("--primary:#0B3D91");
    expect(html).toContain("--dashboard-action-background:#3D63DD");
    expect(html).toContain("--primary-foreground:#FFFFFF");
    expect(html).toContain('src="/api/v1/persona-natural/marca/imagenes/logo-7"');
    expect(html).toContain('src="/api/v1/persona-natural/marca/imagenes/banner-9"');
    expect(html).toContain("Tránsito de Cali");
    // Los tokens vienen ANTES del contenido que los usa.
    expect(html.indexOf("--primary:#0B3D91")).toBeLessThan(html.indexOf("<header"));
    expect(obtenerMarcaPunto).toHaveBeenCalledWith("CALI", expect.objectContaining({ ipCliente: "190.1.2.3" }));
  });

  it("decodifica el código de la ruta antes de pedir la marca", async () => {
    obtenerMarcaPunto.mockResolvedValue(null);
    await paginaHtml("CAL%C3%8D");
    expect(obtenerMarcaPunto.mock.calls[0]?.[0]).toBe("CALÍ");
  });
});

describe("HU #14379 AC3: sin marca el portal se ve como hoy", () => {
  it("sin hoja de estilos, logo y alt de FLIT, sin banner", async () => {
    obtenerMarcaPunto.mockResolvedValue(null);

    const html = await paginaHtml();

    expect(html).not.toContain("data-marca-punto");
    expect(html).not.toContain("--primary:");
    expect(html).toContain('src="/assets/flit/logo-flit.svg"');
    expect(html).toContain('alt="FLIT"');
    expect(html).not.toContain("marca/imagenes");
  });
});

describe("HU #14379: nada se inyecta desde el nombre o la marca", () => {
  it("el nombre con HTML o CSS sale como texto escapado, no como etiquetas", async () => {
    obtenerMarcaPunto.mockResolvedValue(marcaAplicable({ ...BFF, nombre: '<img src=x onerror="alert(1)"></style><script>alert(2)</script>' }));

    const html = await paginaHtml();

    expect(html).not.toContain("<script>alert(2)");
    expect(html).not.toContain("<img src=x onerror");
    expect(html).toContain("&lt;img src=x onerror=");
    // La hoja de estilos solo trae colores.
    const estilo = /<style data-marca-punto[^>]*>([^<]*)<\/style>/.exec(html);
    expect(estilo?.[1]).toBeTruthy();
    expect(estilo?.[1]).not.toMatch(/alert|script|onerror/);
  });

  it("un color malicioso o una imagen externa del BFF no llegan al HTML", async () => {
    obtenerMarcaPunto.mockResolvedValue(marcaAplicable({ ...BFF, colores: { ...BFF.colores, primario: "#000;}body{display:none" } }));
    const sinColores = await paginaHtml();
    expect(sinColores).not.toContain("display:none");
    expect(sinColores).not.toContain("data-marca-punto");

    obtenerMarcaPunto.mockResolvedValue(marcaAplicable({ ...BFF, logoUrl: "https://evil.example/x.png", bannerUrl: "javascript:alert(1)" }));
    const sinImagenes = await paginaHtml();
    expect(sinImagenes).not.toContain("evil.example");
    expect(sinImagenes).not.toContain("javascript:");
    expect(sinImagenes).toContain('src="/assets/flit/logo-flit.svg"');
  });
});

describe("HU #14379 AC5: lo publicado se ve en la siguiente carga", () => {
  it("cada carga vuelve a pedir la marca (la página es dinámica y no la guarda)", async () => {
    obtenerMarcaPunto.mockResolvedValueOnce(MARCA).mockResolvedValueOnce(marcaAplicable({ ...BFF, nombre: "Nombre nuevo", logoUrl: "/api/v1/persona-natural/marca/imagenes/logo-8", version: 5 }));

    const primera = await paginaHtml();
    const segunda = await paginaHtml();

    expect(obtenerMarcaPunto).toHaveBeenCalledTimes(2);
    expect(primera).toContain("logo-7");
    expect(segunda).toContain("logo-8");
    expect(segunda).toContain("Nombre nuevo");
    const pagina = await import("@/app/o/[codigo]/page");
    expect(pagina.dynamic).toBe("force-dynamic");
    expect(pagina).not.toHaveProperty("revalidate");
  });
});

describe("HU #14379 AC2: la misma marca en todos los pasos del punto", () => {
  it("entrada y wizard usan logo y nombre del organismo; el banner solo en la entrada", async () => {
    render(
      conQuery(
        <ProveedorMarca marca={MARCA}>
          <CanalPunto codigo="CALI" cliente={clienteFalso()} />
        </ProveedorMarca>,
      ),
    );
    await screen.findByRole("button", { name: "Iniciar trámite" });

    expect(screen.getByRole("img", { name: "Tránsito de Cali" })).toHaveAttribute("src", "/api/v1/persona-natural/marca/imagenes/logo-7");
    expect(screen.getByTestId("nombre-marca")).toHaveTextContent("Tránsito de Cali");
    expect(screen.getByRole("img", { name: "Banner de Tránsito de Cali" })).toHaveAttribute("src", "/api/v1/persona-natural/marca/imagenes/banner-9");

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "ABC123" } });
    fireEvent.change(screen.getByLabelText("Número de documento"), { target: { value: "1020456789" } });
    fireEvent.click(screen.getByRole("button", { name: "Tipo de Trámite" }));
    fireEvent.click(await screen.findByRole("option", { name: "Traspaso" }));
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

    await screen.findByRole("navigation", { name: "Ruta de navegación" });
    expect(screen.getByLabelText("Wizard del punto")).toBeInTheDocument();
    // El encabezado de la entrada se retira al abrir el wizard: queda un solo logo, el del wizard.
    await waitFor(() => expect(screen.getAllByRole("img", { name: "Tránsito de Cali" })).toHaveLength(1));
    expect(screen.getByRole("img", { name: "Tránsito de Cali" })).toHaveAttribute("src", "/api/v1/persona-natural/marca/imagenes/logo-7");
    expect(screen.getByTestId("nombre-marca")).toHaveTextContent("Tránsito de Cali");
    expect(screen.queryByRole("img", { name: "Banner de Tránsito de Cali" })).not.toBeInTheDocument();
  });

  it("sin logo propio usa el logo FLIT y conserva el nombre del organismo", async () => {
    render(
      conQuery(
        <ProveedorMarca marca={marcaAplicable({ ...BFF, logoUrl: null, bannerUrl: null })}>
          <CanalPunto codigo="CALI" cliente={clienteFalso()} />
        </ProveedorMarca>,
      ),
    );
    await screen.findByRole("button", { name: "Iniciar trámite" });
    expect(screen.getByRole("img", { name: "Tránsito de Cali" })).toHaveAttribute("src", "/assets/flit/logo-flit.svg");
    expect(screen.queryByRole("img", { name: /Banner/ })).not.toBeInTheDocument();
  });
});

describe("HU #14379 AC4: sin cruce con la web de persona ni con otros organismos", () => {
  it("fuera de un punto con marca el logo del portal sigue siendo el de FLIT", async () => {
    render(conQuery(<CanalPunto codigo="OTRO" cliente={clienteFalso()} />));
    await screen.findByRole("button", { name: "Iniciar trámite" });
    expect(screen.getByRole("img", { name: "FLIT" })).toHaveAttribute("src", "/assets/flit/logo-flit.svg");
    expect(screen.queryByTestId("nombre-marca")).not.toBeInTheDocument();
  });

  it("la marca de un organismo se pide con SU código: otro código no recibe la anterior", async () => {
    obtenerMarcaPunto.mockImplementation(async (codigo: string) => (codigo === "CALI" ? MARCA : null));

    const cali = await paginaHtml("CALI");
    const otro = await paginaHtml("PASTO");

    expect(cali).toContain("--primary:#0B3D91");
    expect(otro).not.toContain("--primary:#0B3D91");
    expect(otro).not.toContain("logo-7");
  });

  it("la web de persona (layout y landing) no lee ni lleva la hoja de marca", () => {
    const fuentes = readFileSync("app/layout.tsx", "utf8") + readFileSync("app/page.tsx", "utf8");
    expect(fuentes).not.toMatch(/EstiloMarca|marca\.server|ProveedorMarca/);
  });
});

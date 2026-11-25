import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { SelectorEmpresa } from "@/components/SelectorEmpresa";
import { apiFetch } from "@/lib/api";
import { PARAM_EMPRESA, conEmpresa, hrefConEmpresa } from "@/lib/empresaSeleccionada";

// Uso de ejemplo: <SelectorEmpresa /> en la barra del Super Admin. Lista las compañías, guarda la elegida en ?empresa= de
// la URL (conserva el resto de la query) y los fetchers la propagan: apiFetch("/x", { empresa }) → /api/v1/impuestos/x?empresa=…

const replace = vi.fn();
let busqueda = new URLSearchParams();
let pathname = "/liquidaciones";
vi.mock("next/navigation", () => ({
  usePathname: () => pathname,
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

const pagina = (items: { id: string; nombre: string; nit: string }[], page: number, total: number) => ({ items, total, page, pageSize: 2 });
const UNO = { id: "e-1", nombre: "Empresa Uno", nit: "900000001" };
const DOS = { id: "e-2", nombre: "Empresa Dos", nit: "900000002" };
const TRES = { id: "e-3", nombre: "Empresa Tres", nit: "900000003" };

/** Dos páginas de compañías (pageSize 2, total 3). */
function dosPaginas() {
  return vi.fn(async (u: RequestInfo | URL) => {
    const url = new URL(String(u), "http://app");
    return Response.json(url.searchParams.get("page") === "2" ? pagina([TRES], 2, 3) : pagina([UNO, DOS], 1, 3));
  });
}

beforeEach(() => {
  replace.mockReset();
  busqueda = new URLSearchParams();
  pathname = "/liquidaciones";
});
afterEach(() => vi.unstubAllGlobals());

describe("SelectorEmpresa", () => {
  it("mientras carga lo dice dentro del propio control (placeholder, deshabilitado, aria-busy) sin líneas debajo", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));

    const { container } = render(<SelectorEmpresa />);

    const combo = screen.getByRole("combobox", { name: "Compañía" });
    expect(combo).toBeDisabled();
    expect(combo).toHaveAttribute("placeholder", "Cargando compañías…");
    expect(container.firstElementChild).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByText("Cargando compañías…")).toBeNull();
  });

  it("vive en la barra: ancho w-40 sm:w-56", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));

    const { container } = render(<SelectorEmpresa />);

    expect(container.firstElementChild).toHaveClass("w-40", "sm:w-56");
    expect(container.firstElementChild).not.toHaveClass("w-56");
  });

  it("«Quitar selección» mide al menos 24 × 24 px (WCAG 2.5.8), como en Diagnóstico", async () => {
    busqueda = new URLSearchParams("empresa=e-2");
    vi.stubGlobal("fetch", dosPaginas());

    render(<SelectorEmpresa />);

    const quitar = await screen.findByRole("button", { name: "Quitar selección" });
    const raiz = quitar.closest("[class*='min-w-6']");
    expect(raiz?.className).toContain("[&_button[aria-label^='Quitar']]:min-w-6");
    expect(raiz?.className).toContain("[&_button[aria-label^='Quitar']]:min-h-6");
  });

  it("lista todas las compañías (recorre las páginas) con «Todas las compañías» por defecto", async () => {
    const fetchMock = dosPaginas();
    vi.stubGlobal("fetch", fetchMock);

    render(<SelectorEmpresa />);

    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).toBeEnabled());
    expect(fetchMock.mock.calls.map(([u]) => String(u))).toEqual(["/api/v1/impuestos/backoffice/empresas?page=1", "/api/v1/impuestos/backoffice/empresas?page=2"]);
    fireEvent.click(combo);
    const opciones = screen.getAllByRole("option").map((o) => o.textContent);
    expect(opciones[0]).toContain("Todas las compañías");
    expect(opciones.slice(1).join("|")).toMatch(/Empresa Uno.*\|.*Empresa Dos.*\|.*Empresa Tres/);
  });

  it("muestra elegida la compañía de ?empresa= de la URL", async () => {
    busqueda = new URLSearchParams("empresa=e-2");
    vi.stubGlobal("fetch", dosPaginas());

    render(<SelectorEmpresa />);

    await waitFor(() => expect(screen.getByRole("combobox", { name: "Compañía" })).toHaveValue("Empresa Dos"));
  });

  it("al elegir una compañía la guarda en ?empresa=, conserva los filtros y reinicia la página", async () => {
    busqueda = new URLSearchParams("estado=TERMINADA&page=3");
    vi.stubGlobal("fetch", dosPaginas());

    render(<SelectorEmpresa />);
    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).toBeEnabled());
    fireEvent.click(combo);
    fireEvent.click(screen.getByRole("option", { name: /Empresa Dos/ }));

    expect(replace).toHaveBeenCalledWith("/liquidaciones?estado=TERMINADA&empresa=e-2");
  });

  it("«Todas las compañías» quita ?empresa= de la URL (vista global)", async () => {
    busqueda = new URLSearchParams("empresa=e-1&estado=TERMINADA");
    vi.stubGlobal("fetch", dosPaginas());

    render(<SelectorEmpresa />);
    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).toHaveValue("Empresa Uno"));
    fireEvent.click(combo);
    fireEvent.click(screen.getByRole("option", { name: /Todas las compañías/ }));

    expect(replace).toHaveBeenCalledWith("/liquidaciones?estado=TERMINADA");
  });

  it("si falla lo dice en el propio control (tono warning) con Reintentar de 32 px en la misma fila y, al reintentar, carga la lista", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ error: "mensaje del servidor que no debe verse" }, { status: 500 }))
      .mockImplementation(async () => Response.json(pagina([UNO], 1, 1)));
    vi.stubGlobal("fetch", fetchMock);

    const { container } = render(<SelectorEmpresa />);

    const reintentar = await screen.findByRole("button", { name: "Reintentar cargar compañías" });
    expect(reintentar).toHaveTextContent("Reintentar");
    expect(reintentar).toHaveClass("min-h-8", "text-[color:var(--di-warning)]");
    const combo = screen.getByRole("combobox", { name: "Compañía" });
    expect(combo).toBeDisabled();
    expect(combo).toHaveAttribute("placeholder", "No se cargaron las compañías");
    // Fila única: el combobox y el botón comparten la fila; nada debajo, ni alerta ni el mensaje del servidor.
    expect(reintentar.parentElement).toHaveClass("flex", "items-center", "gap-1");
    expect(reintentar.parentElement).toContainElement(combo);
    expect(screen.queryByRole("alert")).toBeNull();
    expect(container).not.toHaveTextContent("mensaje del servidor que no debe verse");
    // Se anuncia sin crecer en alto: región role="status" solo para el lector, con la frase completa.
    const anuncio = screen.getAllByRole("status").find((el) => el.textContent === "No se cargaron las compañías");
    expect(anuncio).toBeDefined();
    expect(anuncio).toHaveClass("sr-only");
    expect(anuncio).toHaveAttribute("aria-live", "polite");
    expect(anuncio).toHaveAttribute("aria-atomic", "true");
    expect(anuncio).toHaveTextContent("No se cargaron las compañías");

    fireEvent.click(reintentar);
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Compañía" })).toBeEnabled());
    expect(screen.queryByRole("button", { name: "Reintentar cargar compañías" })).toBeNull();
    expect(screen.queryByText("No se cargaron las compañías")).toBeNull();
  });

  it("sin compañías no pone texto debajo, deja solo la vista global y la búsqueda dice «No hay compañías con Impuestos»", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json(pagina([], 1, 0))));

    render(<SelectorEmpresa />);

    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).toBeEnabled());
    expect(screen.queryByText(/No hay compañías con Impuestos/)).toBeNull();
    fireEvent.click(combo);
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Todas las compañías"]);
    fireEvent.change(combo, { target: { value: "zzz" } });
    expect(screen.getByText("No hay compañías con Impuestos")).toBeInTheDocument();
  });
});

describe("empresa seleccionada en los fetchers", () => {
  it("conEmpresa agrega ?empresa= o &empresa= y no toca la ruta sin compañía", () => {
    expect(PARAM_EMPRESA).toBe("empresa");
    expect(conEmpresa("/liquidaciones", "e-1")).toBe("/liquidaciones?empresa=e-1");
    expect(conEmpresa("/liquidaciones?estado=A", "e 1")).toBe("/liquidaciones?estado=A&empresa=e+1");
    expect(conEmpresa("/liquidaciones?estado=A", null)).toBe("/liquidaciones?estado=A");
    expect(conEmpresa("/liquidaciones", "")).toBe("/liquidaciones");
  });

  it("hrefConEmpresa conserva la compañía al navegar entre pantallas", () => {
    expect(hrefConEmpresa("/reportes", "e-1")).toBe("/reportes?empresa=e-1");
    expect(hrefConEmpresa("/reportes", null)).toBe("/reportes");
  });

  it("apiFetch con la opción empresa la propaga a la API por el proxy", async () => {
    const fetchMock = vi.fn(async () => Response.json({ items: [] }));
    vi.stubGlobal("fetch", fetchMock);

    await apiFetch("/liquidaciones?estado=A", { empresa: "e-7" });
    await apiFetch("/liquidaciones");

    expect(fetchMock.mock.calls.map((c) => String((c as unknown as [string])[0]))).toEqual([
      "/api/v1/impuestos/liquidaciones?estado=A&empresa=e-7",
      "/api/v1/impuestos/liquidaciones",
    ]);
  });
});

describe("recetas del selector en components/comun/estilos.ts (spec-visual §0.2)", () => {
  it("QUITAR_24PX_CLS y el Reintentar de la barra salen de las recetas comunes", async () => {
    const estilos = await import("@/components/comun/estilos");
    expect(estilos.QUITAR_24PX_CLS).toBe("[&_button[aria-label^='Quitar']]:min-w-6 [&_button[aria-label^='Quitar']]:min-h-6");
    expect(estilos.BOTON_REINTENTAR_BARRA_CLS).toContain(estilos.BOTON_REINTENTAR_CLS);
    expect(estilos.BOTON_REINTENTAR_BARRA_CLS).toContain("min-h-8");
    expect(estilos.BOTON_REINTENTAR_BARRA_CLS).toContain("text-[color:var(--di-warning)]");
  });
});

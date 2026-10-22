import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ListadoDiagnosticos } from "@/components/listado/ListadoDiagnosticos";
import { diagnostico, lista } from "@/lib/diagnosticos/__tests__/datosListado";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <ListadoDiagnosticos /> en el inicio (/). Lee los filtros de la URL (?desde=&hasta=&modo=&estado=&nombre=
// &pagina=), pide GET /diagnosticos con ellos y pinta la tabla; cambiar un filtro reescribe la URL con router.replace y
// vuelve a la página 1. Clic en una fila abre /diagnosticos/{id} (o /diagnosticos/{id}/carga si está sin lanzar).

const h = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => h.params,
  useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }),
}));

beforeEach(() => {
  h.replace.mockReset();
  h.push.mockReset();
  h.params = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

const consultas = (fetchMock: { mock: { calls: unknown[][] } }) =>
  llamadas(fetchMock, "GET", "/diagnosticos").map(([u]) => Object.fromEntries(new URL(String(u), "http://localhost").searchParams));

const tabla = () => screen.findByRole("table", { name: "Diagnósticos de la compañía" });

describe("ListadoDiagnosticos — AC1: tabla con las columnas y la fila abre el tablero", () => {
  it("pinta ID, nombre, fecha (hora Colombia), tipo, trámite, estado y creador", async () => {
    const d = diagnostico(1);
    simularApi({ "GET /diagnosticos": () => Response.json(lista([d, diagnostico(2, { modo: "SIMPLE", tipo_tramite: null, estado: "COMPLETADO" })])) });
    render(<ListadoDiagnosticos />);

    const t = await tabla();
    const cabeceras = within(t).getAllByRole("columnheader").map((c) => c.textContent);
    expect(cabeceras).toEqual(["ID", "Nombre", "Creado", "Tipo", "Trámite", "Estado", "Creador"]);
    within(t).getAllByRole("columnheader").forEach((c) => expect(c).toHaveAttribute("scope", "col"));

    const fila = within(t).getByRole("row", { name: /Flota de prueba 001/ });
    expect(within(fila).getByText("0192F0A1")).toBeInTheDocument();
    expect(within(fila).getByText("22/10/2026 09:00")).toBeInTheDocument();
    expect(within(fila).getByText("Con Análisis IA")).toBeInTheDocument();
    expect(within(fila).getByText("TRASPASO")).toBeInTheDocument();
    expect(within(fila).getByText("En proceso")).toBeInTheDocument();
    expect(within(fila).getByText("Ana Prueba")).toBeInTheDocument();

    const simple = within(t).getByRole("row", { name: /Flota de prueba 002/ });
    expect(within(simple).getByText("Diagnóstico Simple")).toBeInTheDocument();
    expect(within(simple).getByText("Completado")).toBeInTheDocument();
    expect(within(simple).getByLabelText("Sin trámite")).toBeInTheDocument();
  });

  it("clic en la fila abre su tablero y el nombre es un enlace al tablero (Enter con el teclado)", async () => {
    const d = diagnostico(1);
    simularApi({ "GET /diagnosticos": () => Response.json(lista([d])) });
    render(<ListadoDiagnosticos />);
    const fila = within(await tabla()).getByRole("row", { name: /Flota de prueba 001/ });

    expect(within(fila).getByRole("link", { name: "Flota de prueba 001" })).toHaveAttribute("href", `/diagnosticos/${d.id}`);
    fireEvent.click(within(fila).getByText("Ana Prueba"));
    expect(h.push).toHaveBeenCalledWith(`/diagnosticos/${d.id}`);
  });

  it("sin filtros en la URL pide la página 1 sin filtros", async () => {
    const fetchMock = simularApi({ "GET /diagnosticos": () => Response.json(lista([diagnostico(1)])) });
    render(<ListadoDiagnosticos />);
    await tabla();
    expect(consultas(fetchMock)[0]).toEqual({ pagina: "1", tamano: "25" });
  });

  it("mientras carga muestra el loader; si falla, el aviso con Reintentar", async () => {
    let n = 0;
    simularApi({ "GET /diagnosticos": () => (++n === 1 ? Response.json({ error: "x" }, { status: 500 }) : Response.json(lista([diagnostico(1)]))) });
    render(<ListadoDiagnosticos />);
    expect(screen.getByRole("status", { name: "Cargando los diagnósticos…" })).toBeInTheDocument();
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await tabla()).toBeInTheDocument();
  });
});

describe("ListadoDiagnosticos — AC2: filtros en la URL y vuelta a la página 1", () => {
  it("filtrar por estado Completado y un nombre actualiza la URL, pide la página 1 y muestra solo lo devuelto", async () => {
    h.params = new URLSearchParams("pagina=3");
    const fetchMock = simularApi({
      "GET /diagnosticos": (url) =>
        url.searchParams.get("estado") === "COMPLETADO" && url.searchParams.get("nombre") === "Flota"
          ? Response.json(lista([diagnostico(2, { nombre: "Flota completa", estado: "COMPLETADO" })]))
          : Response.json(lista([diagnostico(1), diagnostico(2, { nombre: "Flota completa", estado: "COMPLETADO" })], { pagina: 3, total: 80 })),
    });
    render(<ListadoDiagnosticos />);
    await tabla();
    expect(consultas(fetchMock)[0]).toMatchObject({ pagina: "3" });

    fireEvent.change(screen.getByLabelText("Estado"), { target: { value: "COMPLETADO" } });
    expect(h.replace).toHaveBeenLastCalledWith("/?estado=COMPLETADO", { scroll: false });

    const antes = consultas(fetchMock).length;
    fireEvent.change(screen.getByLabelText("Nombre"), { target: { value: "Flota" } });
    // Espera de 300 ms al escribir: no consulta en cada tecla.
    expect(consultas(fetchMock).length).toBe(antes);
    await waitFor(() => expect(h.replace).toHaveBeenLastCalledWith("/?estado=COMPLETADO&nombre=Flota", { scroll: false }), { timeout: 2000 });

    await waitFor(() => expect(within(screen.getByRole("table", { name: "Diagnósticos de la compañía" })).queryByText("Flota de prueba 001")).toBeNull());
    expect(consultas(fetchMock).at(-1)).toEqual({ estado: "COMPLETADO", nombre: "Flota", pagina: "1", tamano: "25" });
    expect(within(screen.getByRole("table", { name: "Diagnósticos de la compañía" })).getByText("Flota completa")).toBeInTheDocument();
  });

  it("con los filtros en la URL consulta de entrada con ellos y los muestra en los controles", async () => {
    h.params = new URLSearchParams("desde=2026-10-01&hasta=2026-10-22&modo=SIMPLE&estado=INCOMPLETO&nombre=TST");
    const fetchMock = simularApi({ "GET /diagnosticos": () => Response.json(lista([diagnostico(1, { estado: "INCOMPLETO", modo: "SIMPLE", tipo_tramite: null })])) });
    render(<ListadoDiagnosticos />);
    await tabla();
    expect(consultas(fetchMock)[0]).toEqual({ desde: "2026-10-01", hasta: "2026-10-22", modo: "SIMPLE", estado: "INCOMPLETO", nombre: "TST", pagina: "1", tamano: "25" });
    expect(screen.getByLabelText("Desde")).toHaveValue("2026-10-01");
    expect(screen.getByLabelText("Hasta")).toHaveValue("2026-10-22");
    expect(screen.getByLabelText("Tipo")).toHaveValue("SIMPLE");
    expect(screen.getByLabelText("Estado")).toHaveValue("INCOMPLETO");
    expect(screen.getByLabelText("Nombre")).toHaveValue("TST");
  });

  it("cambiar de página reescribe la URL con la página; la paginación es una navegación con nombre", async () => {
    simularApi({ "GET /diagnosticos": (url) => Response.json(lista([diagnostico(Number(url.searchParams.get("pagina")))], { total: 60, pagina: Number(url.searchParams.get("pagina")) })) });
    render(<ListadoDiagnosticos />);
    await tabla();
    const nav = screen.getByRole("navigation", { name: "Paginación de diagnósticos" });
    fireEvent.click(within(nav).getByRole("button", { name: "Página 2" }));
    expect(h.replace).toHaveBeenLastCalledWith("/?pagina=2", { scroll: false });
  });

  it("400 con campo marca el filtro: RANGO_INVALIDO deja Hasta inválido con el mensaje junto al campo", async () => {
    h.params = new URLSearchParams("desde=2026-10-22&hasta=2026-10-01");
    simularApi({ "GET /diagnosticos": () => Response.json({ code: "RANGO_INVALIDO", error: "hasta < desde", campo: "hasta" }, { status: 400 }) });
    render(<ListadoDiagnosticos />);
    const hasta = screen.getByLabelText("Hasta");
    await waitFor(() => expect(hasta).toHaveAttribute("aria-invalid", "true"));
    expect(document.getElementById(hasta.getAttribute("aria-describedby")!)).toHaveTextContent(/anterior/i);
    expect(screen.getByLabelText("Desde")).not.toHaveAttribute("aria-invalid", "true");
  });

  it("403 lleva a la página de acceso negado", async () => {
    simularApi({ "GET /diagnosticos": () => new Response(null, { status: 403 }) });
    render(<ListadoDiagnosticos />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISOS_DIAGNOSTICO"));
  });
});

describe("ListadoDiagnosticos — AC3: lote sin lanzar", () => {
  it("lleva el chip «Sin lanzar» y abre su parametrización", async () => {
    const d = diagnostico(3, { estado: "CREADO", nombre: "Lote por lanzar" });
    simularApi({ "GET /diagnosticos": () => Response.json(lista([d])) });
    render(<ListadoDiagnosticos />);
    const fila = within(await tabla()).getByRole("row", { name: /Lote por lanzar/ });
    expect(within(fila).getByText("Sin lanzar")).toBeInTheDocument();
    expect(within(fila).getByRole("link", { name: "Lote por lanzar" })).toHaveAttribute("href", `/diagnosticos/${d.id}/carga`);
    fireEvent.click(within(fila).getByText("Sin lanzar"));
    expect(h.push).toHaveBeenCalledWith(`/diagnosticos/${d.id}/carga`);
  });

  it("el filtro de estado no ofrece «Sin lanzar» (la API no filtra por CREADO)", async () => {
    simularApi({ "GET /diagnosticos": () => Response.json(lista([diagnostico(1)])) });
    render(<ListadoDiagnosticos />);
    await tabla();
    const opciones = within(screen.getByLabelText("Estado")).getAllByRole("option").map((o) => (o as HTMLOptionElement).value);
    expect(opciones).toEqual(["", "EN_PROCESO", "COMPLETADO", "INCOMPLETO"]);
  });
});

describe("ListadoDiagnosticos — AC4: creador «No disponible»", () => {
  it("la columna muestra «No disponible» y el resto de la fila se ve completo", async () => {
    simularApi({
      "GET /diagnosticos": () => Response.json(lista([diagnostico(1, { usuario_creador: { id: "00000000-0000-7000-8000-0000000000c9", nombre: "No disponible" } })])),
    });
    render(<ListadoDiagnosticos />);
    const fila = within(await tabla()).getByRole("row", { name: /Flota de prueba 001/ });
    const celdas = within(fila).getAllByRole("cell");
    expect(celdas).toHaveLength(7);
    expect(celdas[6]).toHaveTextContent("No disponible");
    for (const texto of ["0192F0A1", "Flota de prueba 001", "22/10/2026 09:00", "Con Análisis IA", "TRASPASO", "En proceso"])
      expect(within(fila).getByText(texto)).toBeInTheDocument();
  });
});

describe("ListadoDiagnosticos — AC5 (negativo): sin resultados con filtros vs compañía sin diagnósticos", () => {
  it("con filtros y sin resultados: «No hay diagnósticos con estos filtros» con «Limpiar filtros», no el vacío de compañía", async () => {
    h.params = new URLSearchParams("estado=COMPLETADO&nombre=zzz");
    const fetchMock = simularApi({ "GET /diagnosticos": () => Response.json(lista([])) });
    render(<ListadoDiagnosticos />);
    expect(await screen.findByText("No hay diagnósticos con estos filtros")).toBeInTheDocument();
    expect(screen.queryByText("Todavía no hay diagnósticos")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(h.replace).toHaveBeenLastCalledWith("/", { scroll: false });
    await waitFor(() => expect(consultas(fetchMock).at(-1)).toEqual({ pagina: "1", tamano: "25" }));
    expect(screen.getByLabelText("Estado")).toHaveValue("");
    expect(screen.getByLabelText("Nombre")).toHaveValue("");
  });

  it("sin filtros y sin resultados: «Todavía no hay diagnósticos» y sin «Limpiar filtros»", async () => {
    simularApi({ "GET /diagnosticos": () => Response.json(lista([])) });
    render(<ListadoDiagnosticos />);
    expect(await screen.findByText("Todavía no hay diagnósticos")).toBeInTheDocument();
    expect(screen.queryByText("No hay diagnósticos con estos filtros")).toBeNull();
    expect(screen.queryByRole("button", { name: "Limpiar filtros" })).toBeNull();
  });
});

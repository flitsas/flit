import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { TablaCompanias } from "@/components/usuarios/TablaCompanias";
import type { FiltrosEmpresas } from "@/lib/usuarios/tipos";

// Uso de ejemplo: <TablaCompanias filtros={{ busqueda: "", usuarios: null, pagina: 1, tamano: 20 }} onCambiar={aplicar} />
// paso 1 del SuperAdmin (spec-ui §10.2): GET /backoffice/usuarios/empresas, tabla Compañía · NIT · Usuarios ·
// Administradores · Acción, buscador por nombre o NIT y filtro «Usuarios» (sin filtro de estado, H-U3). Cada fila lleva
// a /usuarios?empresa=<id>. Cuatro estados. Datos ficticios.

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push, replace: vi.fn(), refresh: vi.fn() }) }));

const DEMO = { id: "00000000-0000-4000-8000-0000000000e1", nombre: "Compañía Demo", nit: "900000001", estado: "ACTIVA", usuarios_diagnostico: 4, administradores_diagnostico: 1 };
const OTRA = { id: "00000000-0000-4000-8000-0000000000e2", nombre: "Compañía Ejemplo", nit: null, estado: "ACTIVA", usuarios_diagnostico: 0, administradores_diagnostico: 0 };

const FILTROS = { busqueda: "", usuarios: null, pagina: 1, tamano: 20 } as const;
const lista = (empresas: unknown[]) => Response.json({ pagina: 1, tamano: 20, total: empresas.length, empresas });

beforeEach(() => push.mockReset());
afterEach(() => vi.unstubAllGlobals());

describe("HU14588 TablaCompanias", () => {
  it("cargando: estado con role=status «Cargando compañías…»", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));
    render(<TablaCompanias filtros={FILTROS} onCambiar={vi.fn()} />);
    expect(screen.getByRole("status")).toHaveTextContent("Cargando compañías…");
  });

  it("lleno: columnas, NIT o «Sin NIT», conteos y enlace «Gestionar usuarios de …» a /usuarios?empresa=<id>", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([DEMO, OTRA])));
    render(<TablaCompanias filtros={FILTROS} onCambiar={vi.fn()} />);
    const tabla = await screen.findByRole("table");
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Compañía", "NIT", "Usuarios", "Administradores", "Acción"]);
    const enlace = within(tabla).getByRole("link", { name: "Gestionar usuarios de Compañía Demo" });
    expect(enlace.getAttribute("href")).toBe(`/usuarios?empresa=${DEMO.id}`);
    const filaOtra = within(tabla).getAllByRole("row").find((r) => r.textContent?.includes("Compañía Ejemplo"))!;
    expect(filaOtra).toHaveTextContent("Sin NIT");
  });

  it("clic en la fila (fuera del enlace) navega a la compañía", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([DEMO])));
    render(<TablaCompanias filtros={FILTROS} onCambiar={vi.fn()} />);
    const tabla = await screen.findByRole("table");
    const fila = within(tabla).getAllByRole("row").find((r) => r.textContent?.includes("Compañía Demo"))!;
    fireEvent.click(within(fila).getByText("900000001"));
    expect(push).toHaveBeenCalledWith(`/usuarios?empresa=${DEMO.id}`);
  });

  it("los filtros de la tabla viajan en el enlace para volver a ellos por las migas", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([DEMO])));
    render(<TablaCompanias filtros={{ ...FILTROS, busqueda: "demo", usuarios: "CON" }} onCambiar={vi.fn()} />);
    const enlace = await screen.findByRole("link", { name: "Gestionar usuarios de Compañía Demo" });
    const q = new URL(enlace.getAttribute("href")!, "http://x").searchParams;
    expect(q.get("empresa")).toBe(DEMO.id);
    expect(q.get("busqueda")).toBe("demo");
    expect(q.get("usuarios")).toBe("CON");
  });

  it("no ofrece filtro de estado (H-U3); sí «Usuarios» con Todas / Con usuarios / Sin usuarios", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([DEMO])));
    const onCambiar = vi.fn();
    render(<TablaCompanias filtros={FILTROS} onCambiar={onCambiar} />);
    await screen.findByRole("table");
    expect(screen.queryByRole("combobox", { name: /^Estado/ })).toBeNull();
    const usuarios = screen.getByRole("combobox", { name: "Usuarios" });
    fireEvent.change(usuarios, { target: { value: "SIN" } });
    expect(onCambiar).toHaveBeenCalledWith(expect.objectContaining({ usuarios: "SIN" }));
  });

  it("vacío real: «Ninguna compañía tiene Diagnóstico activo» sin CTA", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([])));
    render(<TablaCompanias filtros={FILTROS} onCambiar={vi.fn()} />);
    expect(await screen.findByText("Ninguna compañía tiene Diagnóstico activo")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Limpiar filtros" })).toBeNull();
  });

  it("vacío por filtro: «Ninguna compañía coincide» con «Limpiar filtros»", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([])));
    const onCambiar = vi.fn();
    render(<TablaCompanias filtros={{ ...FILTROS, busqueda: "zzz" }} onCambiar={onCambiar} />);
    expect(await screen.findByText("Ninguna compañía coincide")).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" }).at(-1)!);
    expect(onCambiar).toHaveBeenCalledWith(expect.objectContaining({ busqueda: "", usuarios: null }));
  });

  it("error: alerta con «Reintentar» que vuelve a pedir la lista", async () => {
    let n = 0;
    const fetchMock = vi.fn(async () => (n++ === 0 ? Response.json({ code: "IDENTIDAD_NO_DISPONIBLE" }, { status: 503 }) : lista([DEMO])));
    vi.stubGlobal("fetch", fetchMock);
    render(<TablaCompanias filtros={FILTROS} onCambiar={vi.fn()} />);
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    await screen.findByRole("table");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("la búsqueda espera a que se deje de escribir y emite busqueda", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => lista([DEMO])));
    const onCambiar = vi.fn();
    render(<TablaCompanias filtros={FILTROS} onCambiar={onCambiar} />);
    await screen.findByRole("table");
    fireEvent.change(screen.getByRole("textbox", { name: "Buscar por nombre o NIT" }), { target: { value: "9000" } });
    await waitFor(() => expect(onCambiar).toHaveBeenCalledWith(expect.objectContaining({ busqueda: "9000" })));
  });
});

// D-6: paginación en el servidor con el Pagination del kit (página, tamaño, total y selector de tamaño).
describe("HU14588 TablaCompanias — paginación en el servidor (D-6)", () => {
  const muchas = (pagina: number, tamano: number) =>
    Response.json({
      pagina,
      tamano,
      total: 60,
      empresas: Array.from({ length: 2 }, (_, i) => ({ ...DEMO, id: `00000000-0000-4000-8000-0000000001${pagina}${i}`, nombre: `Compañía ${pagina}-${i}` })),
    });

  it("pide pagina y tamano de los filtros, cambia de página y de tamaño y muestra «N de M»", async () => {
    const fetchMock = vi.fn(async (u: RequestInfo | URL) => {
      const q = new URL(String(u), "http://x").searchParams;
      return muchas(Number(q.get("pagina")), Number(q.get("tamano")));
    });
    vi.stubGlobal("fetch", fetchMock);
    const onCambiar = vi.fn();
    const filtros: FiltrosEmpresas = { busqueda: "", usuarios: null, pagina: 1, tamano: 25 };
    const { rerender } = render(<TablaCompanias filtros={filtros} onCambiar={onCambiar} />);
    await screen.findByRole("table");
    const q1 = new URL(String(fetchMock.mock.calls.at(-1)![0]), "http://x").searchParams;
    expect([q1.get("pagina"), q1.get("tamano")]).toEqual(["1", "25"]);
    expect(screen.getByText("Mostrando 2 de 60 compañías")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Página 2" }));
    expect(onCambiar).toHaveBeenLastCalledWith({ pagina: 2 });
    fireEvent.change(screen.getByRole("combobox", { name: "Filas por página" }), { target: { value: "50" } });
    expect(onCambiar).toHaveBeenLastCalledWith({ tamano: 50, pagina: 1 });
    rerender(<TablaCompanias filtros={{ ...filtros, pagina: 2 }} onCambiar={onCambiar} />);
    await screen.findByText("Compañía 2-0");
    const q2 = new URL(String(fetchMock.mock.calls.at(-1)![0]), "http://x").searchParams;
    expect([q2.get("pagina"), q2.get("tamano")]).toEqual(["2", "25"]);
  });
});


// Página de Consultas (SuperAdmin y empresa) y la carrera de peticiones de la consola.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { SesionComparendos } from "@/lib/sesion";

const sesion = vi.hoisted(() => ({ actual: null as SesionComparendos | null }));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: sesion.actual, cargando: false, error: null }),
}));
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

import Page from "@/app/(app)/reportes/consultas/page";
import { QueryConsole } from "@/components/consultas-personalizadas/QueryConsole";
import { COMPARENDO_DEFAULT_COLUMNS, COMPARENDO_PRESETS, comparendoColumns } from "@/components/consultas-personalizadas/comparendo-columns";
import type { ComparendoQueryRow, QueryField, QueryResult, QuerySource } from "@/lib/api/consultas-personalizadas";
import { fila, instalarServidor } from "./mockServidor";

const CAPACIDADES = { derechosPeticion: true, consultaContraventor: true, cambioContraventor: true, ocr: true, reportes: true };

beforeEach(() => {
  window.history.replaceState(null, "", "/reportes/consultas");
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe("cmp-query · página", () => {
  it("cmp-query: el SuperAdmin sin empresa consulta todas y ve la columna Empresa", async () => {
    sesion.actual = { usuario: { id: "u", email: "s@x", esSuperAdmin: true }, permisos: [], empresa: null };
    const { peticiones } = instalarServidor();
    render(<Page />);
    const tabla = await screen.findByTestId("cmp-query-tabla");
    expect(within(tabla).getByRole("columnheader", { name: "Empresa" })).toBeInTheDocument();
    expect(screen.getByText("Sin empresa elegida se consulta sobre todas.")).toBeInTheDocument();
    const corrida = peticiones.find((p) => p.ruta === "/run")!;
    expect(corrida.query.get("empresa")).toBeNull();
  });

  it("cmp-query: con «todas», la columna Empresa sigue al aplicar un preset o abrir una del sistema", async () => {
    sesion.actual = { usuario: { id: "u", email: "s@x", esSuperAdmin: true }, permisos: [], empresa: null };
    instalarServidor();
    render(<Page />);
    await screen.findByTestId("cmp-query-tabla");
    fireEvent.click(screen.getByRole("button", { name: "Plazos" }));
    expect(within(screen.getByTestId("cmp-query-tabla")).getByRole("columnheader", { name: "Empresa" })).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("cmp-query-guardada-c0000000-0000-4000-8000-000000000003"));
    expect(await within(screen.getByTestId("cmp-query-tabla")).findByRole("columnheader", { name: "Límite de gestión" })).toBeInTheDocument();
    expect(within(screen.getByTestId("cmp-query-tabla")).getByRole("columnheader", { name: "Empresa" })).toBeInTheDocument();
  });

  it("cmp-query: un usuario de empresa no puede apuntar a otra con ?empresa= y no ve la columna Empresa", async () => {
    window.history.replaceState(null, "", "/reportes/consultas?empresa=otra");
    sesion.actual = {
      usuario: { id: "u", email: "g@x", esSuperAdmin: false },
      permisos: ["comparendos.reportes.consultas"],
      empresa: { id: "mia", nombre: "Mi empresa", capacidades: CAPACIDADES },
    };
    const { peticiones } = instalarServidor();
    render(<Page />);
    const tabla = await screen.findByTestId("cmp-query-tabla");
    expect(within(tabla).queryByRole("columnheader", { name: "Empresa" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Empresa")).not.toBeInTheDocument();
    expect(peticiones.filter((p) => p.ruta === "/run").every((p) => p.query.get("empresa") === null)).toBe(true);
  });
});

describe("cmp-query · periodo anterior", () => {
  it("cmp-query: con periodo anterior numérico sí se compara", async () => {
    sesion.actual = { usuario: { id: "u", email: "s@x", esSuperAdmin: true }, permisos: [], empresa: null };
    instalarServidor({ totalPeriodoAnterior: 1 });
    render(<Page />);
    expect(await screen.findByTestId("cmp-query-comparacion")).toHaveTextContent("+100 % frente a los 1 del periodo anterior");
  });
});

describe("cmp-query · carrera de peticiones", () => {
  it("cmp-query: cambiar un filtro y pasar de página enseguida no deja el paginador desfasado", async () => {
    const pedidas: number[] = [];
    const source: QuerySource<ComparendoQueryRow> = {
      testIdPrefix: "cmp-query", dateFields: [{ value: "imposicion", label: "Imposición" }], defaultDateField: "imposicion",
      columnsStorageKey: "k2", exportPrefix: "x", rowNoun: ["comparendo", "comparendos"],
      fetchFields: async () => [] as QueryField[], fetchSaved: async () => [], save: vi.fn(), remove: vi.fn(),
      run: async (_d, { page = 1 }) => {
        pedidas.push(page);
        return { total: 120, page, pageSize: 50, desde: "2026-10-01", hasta: "2026-11-01", totalPeriodoAnterior: null, filas: [fila({ id: `p${page}`, numero: `PAG-${page}` })], cobertura: [] };
      },
    };
    const columnas = (f: QueryField[]) => comparendoColumns(f, { conEmpresa: false });
    render(
      <QueryConsole<ComparendoQueryRow> source={source} columns={columnas} presets={COMPARENDO_PRESETS}
        defaultColumns={COMPARENDO_DEFAULT_COLUMNS} rowKey={(f) => f.id} sheetName="C" ambito="su empresa" />,
    );
    expect(await screen.findByText("PAG-1")).toBeInTheDocument();
    fireEvent.change(screen.getByTestId("cmp-query-preset"), { target: { value: "ultimos_7" } });
    fireEvent.click(screen.getByRole("button", { name: /página 3/i }));
    expect(await screen.findByText("PAG-3")).toBeInTheDocument();
    // Pasan los 250 ms del respiro: no debe volver a pedir la página 1 dejando el paginador en la 3.
    await new Promise((r) => setTimeout(r, 400));
    expect(screen.getByText("PAG-3")).toBeInTheDocument();
    expect(pedidas.at(-1)).toBe(3);
  });

  it("cmp-query: una página vieja que vuelve tarde no pisa el resultado de un filtro nuevo", async () => {
    const pendientes: { page: number; resolver: (r: QueryResult<ComparendoQueryRow>) => void; signal?: AbortSignal }[] = [];
    const resultado = (numero: string): QueryResult<ComparendoQueryRow> => ({
      total: 120, page: 1, pageSize: 50, desde: "2026-10-01", hasta: "2026-11-01", totalPeriodoAnterior: null,
      filas: [fila({ id: numero, numero })], cobertura: [],
    });
    const source: QuerySource<ComparendoQueryRow> = {
      testIdPrefix: "cmp-query", dateFields: [{ value: "imposicion", label: "Imposición" }], defaultDateField: "imposicion",
      columnsStorageKey: "k", exportPrefix: "x", rowNoun: ["comparendo", "comparendos"],
      fetchFields: async () => [] as QueryField[],
      fetchSaved: async () => [],
      save: vi.fn(), remove: vi.fn(),
      run: (_d, { page = 1, signal }) => new Promise((resolver) => pendientes.push({ page, resolver, signal })),
    };
    const columnas = (f: QueryField[]) => comparendoColumns(f, { conEmpresa: false });
    render(
      <QueryConsole<ComparendoQueryRow> source={source} columns={columnas} presets={COMPARENDO_PRESETS}
        defaultColumns={COMPARENDO_DEFAULT_COLUMNS} rowKey={(f) => f.id} sheetName="C" ambito="su empresa" />,
    );
    await waitFor(() => expect(pendientes).toHaveLength(1));
    act(() => pendientes[0].resolver(resultado("PRIMERA")));
    expect(await screen.findByText("PRIMERA")).toBeInTheDocument();

    // Página 2 en vuelo…
    fireEvent.click(screen.getByRole("button", { name: /página 2/i }));
    await waitFor(() => expect(pendientes).toHaveLength(2));
    // …y antes de que vuelva, cambia el periodo: sale otra consulta.
    fireEvent.change(screen.getByTestId("cmp-query-preset"), { target: { value: "ultimos_7" } });
    await waitFor(() => expect(pendientes).toHaveLength(3));
    expect(pendientes[1].signal?.aborted).toBe(true);

    act(() => pendientes[2].resolver(resultado("DEL-FILTRO")));
    expect(await screen.findByText("DEL-FILTRO")).toBeInTheDocument();
    // La página vieja vuelve tarde: no debe pisar lo del filtro.
    await act(async () => pendientes[1].resolver(resultado("PAGINA-VIEJA")));
    expect(screen.queryByText("PAGINA-VIEJA")).not.toBeInTheDocument();
    expect(screen.getByText("DEL-FILTRO")).toBeInTheDocument();
  });
});

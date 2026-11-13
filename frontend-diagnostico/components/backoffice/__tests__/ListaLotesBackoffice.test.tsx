import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ListaLotesBackoffice } from "@/components/backoffice/ListaLotesBackoffice";
import { EMPRESA_A, listaLotes, lote, simularApi } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <ListaLotesBackoffice empresa="" estado={null} conErrores={false} pagina={1} /> en /backoffice/diagnosticos:
// sin compañía elegida no consulta lotes; la compañía se elige en el selector con búsqueda por nombre o NIT (GET
// /backoffice/empresas, el mismo del editor de contrato) y entonces pide GET /backoffice/diagnosticos?empresa= con los
// filtros de estado y «con errores» y lista los lotes con un enlace a su grilla (?empresa= incluido).

const h = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/diagnosticos", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => h.replace.mockReset());
afterEach(() => vi.unstubAllGlobals());

const EMPRESA_B = "00000000-0000-7000-8000-0000000000b2";
/** GET /backoffice/empresas (inventadas, ya ordenadas por nombre): A y B. */
const EMPRESAS = [
  { id: EMPRESA_B, nombre: "Carga del Norte SAS", nit: null },
  { id: EMPRESA_A, nombre: "Transportes A SAS", nit: "900000010" },
];
const conEmpresas = (rutas: Parameters<typeof simularApi>[0] = {}) =>
  simularApi({ "GET /backoffice/empresas": () => Response.json(EMPRESAS), ...rutas });

const montar = (props: Partial<Parameters<typeof ListaLotesBackoffice>[0]> = {}) =>
  render(<ListaLotesBackoffice empresa="" estado={null} conErrores={false} pagina={1} {...props} />);

/** Busca la compañía por nombre o NIT en el selector y la elige con un clic. */
const elegir = async (busqueda: string, opcion: RegExp) => {
  const selector = await screen.findByRole("combobox", { name: "Empresa" });
  fireEvent.change(selector, { target: { value: busqueda } });
  fireEvent.click(screen.getByRole("option", { name: opcion }));
};

/** Parámetros de cada GET /backoffice/diagnosticos (el listado de empresas no cuenta). */
const consultas = (fetchMock: { mock: { calls: unknown[][] } }) =>
  fetchMock.mock.calls
    .map(([u]) => new URL(String(u), "http://localhost"))
    .filter((u) => u.pathname.endsWith("/backoffice/diagnosticos"))
    .map((u) => Object.fromEntries(u.searchParams));

describe("ListaLotesBackoffice — AC1: compañía elegida y filtro «con errores»", () => {
  it("sin compañía elegida no consulta lotes y lo dice", async () => {
    const fetchMock = conEmpresas();
    montar();
    expect(screen.getByText(/elige una compañía/i)).toBeInTheDocument();
    await screen.findByRole("combobox", { name: "Empresa" });
    expect(consultas(fetchMock)).toHaveLength(0);
  });

  it("elige A y filtra «con errores»: pide con empresa=A y con_errores=true y ve solo los lotes devueltos", async () => {
    const fetchMock = conEmpresas({
      "GET /backoffice/diagnosticos": (url) =>
        Response.json(url.searchParams.get("con_errores") === "true" ? listaLotes([lote(1, { nombre: "Lote con fallas", celdas_en_error: 3 })]) : listaLotes([lote(1, { nombre: "Lote con fallas", celdas_en_error: 3 }), lote(2, { nombre: "Lote limpio", celdas_en_error: 0, estado: "COMPLETADO" })])),
    });
    montar();
    await elegir("Transportes", /Transportes A SAS/);
    const tabla = await screen.findByRole("table", { name: /lotes de la compañía/i });
    expect(within(tabla).getByText("Lote limpio")).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Solo lotes con errores"));
    await waitFor(() => expect(within(screen.getByRole("table", { name: /lotes de la compañía/i })).queryByText("Lote limpio")).toBeNull());

    const ultima = consultas(fetchMock).at(-1)!;
    expect(ultima).toMatchObject({ empresa: EMPRESA_A, con_errores: "true" });
    const fila = within(screen.getByRole("table", { name: /lotes de la compañía/i })).getByRole("row", { name: /Lote con fallas/ });
    expect(within(fila).getByText(/3 con error/)).toBeInTheDocument();
    const enlace = within(fila).getByRole("link", { name: "Lote con fallas" });
    expect(enlace).toHaveAttribute("href", `/backoffice/diagnosticos/${lote(1).id}?empresa=${EMPRESA_A}`);
    expect(h.replace).toHaveBeenLastCalledWith(expect.stringContaining("con_errores=true"), expect.anything());
  });

  it("con la compañía en la URL consulta de entrada con sus filtros", async () => {
    const fetchMock = conEmpresas({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([lote(1)])) });
    montar({ empresa: EMPRESA_A, estado: "INCOMPLETO", conErrores: true });
    await screen.findByRole("table", { name: /lotes de la compañía/i });
    expect(consultas(fetchMock)[0]).toEqual({ empresa: EMPRESA_A, estado: "INCOMPLETO", con_errores: "true", pagina: "1" });
  });

  it("paginación estándar (HU #13916): selector 10/20/30/50; 10 es el defecto y no viaja, 30 sí", async () => {
    const fetchMock = conEmpresas({
      "GET /backoffice/diagnosticos": (url) => Response.json(listaLotes([lote(1)], { tamano: Number(url.searchParams.get("tamano") ?? 10), total: 60 })),
    });
    montar({ empresa: EMPRESA_A });
    await screen.findByRole("table", { name: /lotes de la compañía/i });
    expect(consultas(fetchMock)[0]).not.toHaveProperty("tamano");
    const selector = screen.getByLabelText("Filas por página") as HTMLSelectElement;
    expect(Array.from(selector.options).map((o) => o.value)).toEqual(["10", "20", "30", "50"]);
    expect(selector).toHaveValue("10");

    fireEvent.change(selector, { target: { value: "30" } });
    await waitFor(() => expect(consultas(fetchMock).at(-1)).toMatchObject({ tamano: "30", pagina: "1" }));
    fireEvent.change(await screen.findByLabelText("Filas por página"), { target: { value: "10" } });
    await waitFor(() => expect(consultas(fetchMock)).toHaveLength(3));
    expect(consultas(fetchMock).at(-1)).not.toHaveProperty("tamano");
  });

  it("vacío: la compañía no tiene lotes con esos filtros", async () => {
    conEmpresas({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([])) });
    montar({ empresa: EMPRESA_A });
    expect(await screen.findByText(/no tiene lotes con estos filtros/i)).toBeInTheDocument();
  });

  it("falla: aviso con Reintentar", async () => {
    let n = 0;
    conEmpresas({ "GET /backoffice/diagnosticos": () => (++n === 1 ? Response.json({ error: "x" }, { status: 500 }) : Response.json(listaLotes([lote(1)]))) });
    montar({ empresa: EMPRESA_A });
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table", { name: /lotes de la compañía/i })).toBeInTheDocument();
  });
});

// Punto 6 (ola 2026-11-11): la compañía se elige en el selector compartido con el editor de contrato, no pegando su uuid.
describe("ListaLotesBackoffice — Punto 6: selector de compañía", () => {
  it("AC1: el selector busca por nombre o NIT (como el editor de contrato) y no hay campo para pegar el uuid", async () => {
    conEmpresas();
    montar();
    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    expect(screen.queryByLabelText("Identificador de la compañía")).toBeNull();
    expect(screen.queryByRole("button", { name: "Ver lotes" })).toBeNull();
    expect(screen.queryByPlaceholderText(/0000-0000/)).toBeNull();

    fireEvent.change(selector, { target: { value: "900000010" } });
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Transportes A SASNIT 900000010"]);
    fireEvent.change(selector, { target: { value: "norte" } });
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Carga del Norte SASsin NIT"]);
  });

  it("AC2: al elegir otra compañía consulta con ?empresa={id} y vuelve a la página 1", async () => {
    const fetchMock = conEmpresas({
      "GET /backoffice/diagnosticos": (url) => Response.json(listaLotes([lote(1)], { pagina: Number(url.searchParams.get("pagina") ?? 1), total: 60 })),
    });
    montar({ empresa: EMPRESA_A, pagina: 3 });
    await screen.findByRole("table", { name: /lotes de la compañía/i });
    expect(consultas(fetchMock)[0]).toMatchObject({ empresa: EMPRESA_A, pagina: "3" });

    await elegir("Norte", /Carga del Norte SAS/);
    await waitFor(() => expect(consultas(fetchMock).at(-1)).toMatchObject({ empresa: EMPRESA_B, pagina: "1" }));
    const url = String(h.replace.mock.calls.at(-1)![0]);
    expect(url).toContain(`empresa=${EMPRESA_B}`);
    expect(url).not.toContain("pagina=");
  });

  it("AC2: con la compañía en la URL (recarga o vuelta del detalle) el selector la muestra ya elegida", async () => {
    conEmpresas({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([lote(1)])) });
    montar({ empresa: EMPRESA_A });
    expect(await screen.findByRole("combobox", { name: "Empresa" })).toHaveValue("Transportes A SAS");
  });

  it("AC3: si Identidad no responde, el selector muestra el error con Reintentar y los lotes de la URL siguen visibles", async () => {
    let intento = 0;
    conEmpresas({
      "GET /backoffice/empresas": () =>
        ++intento === 1 ? Response.json({ code: "IDENTIDAD_NO_DISPONIBLE", error: "Identidad no respondió." }, { status: 503 }) : Response.json(EMPRESAS),
      "GET /backoffice/diagnosticos": () => Response.json(listaLotes([lote(1)])),
    });
    montar({ empresa: EMPRESA_A });
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo cargar el listado de empresas");
    expect(await screen.findByRole("table", { name: /lotes de la compañía/i })).toBeInTheDocument();
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("combobox", { name: "Empresa" })).toHaveValue("Transportes A SAS");
  });

  it("AC3: sin empresas con Diagnóstico habilitado lo dice y no consulta lotes", async () => {
    const fetchMock = conEmpresas({ "GET /backoffice/empresas": () => Response.json([]) });
    montar();
    expect(await screen.findByText("No hay empresas con Diagnóstico habilitado")).toBeInTheDocument();
    expect(consultas(fetchMock)).toHaveLength(0);
  });

  it("HU14562 AC43: cada lote muestra «ID · modo · trámite» (B3) y el express se rotula", async () => {
    conEmpresas({
      "GET /backoffice/diagnosticos": () =>
        Response.json(
          listaLotes([
            lote(1, { modo: "CON_ANALISIS", tipo_tramite: { codigo: "TRASPASO_STANDARD", nombre: "TRASPASO" }, es_express: false }),
            lote(2, { modo: "SIMPLE", tipo_tramite: null, es_express: true }),
          ]),
        ),
    });
    montar({ empresa: EMPRESA_A });
    const tabla = await screen.findByRole("table", { name: /lotes de la compañía/i });
    const filas = within(tabla).getAllByRole("row");
    expect(filas[1]).toHaveTextContent(/00000000-0000-7000-8000-00000000b001 · Con análisis IA · TRASPASO/);
    expect(filas[2]).toHaveTextContent(/00000000-0000-7000-8000-00000000b002 · Simple · Express/);
  });

  it("HU14562 AC43: sin modo (API previo a B3) la fila muestra solo el ID, sin inventar", async () => {
    conEmpresas({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([lote(3)])) });
    montar({ empresa: EMPRESA_A });
    const tabla = await screen.findByRole("table", { name: /lotes de la compañía/i });
    const fila = within(tabla).getAllByRole("row")[1];
    expect(fila).toHaveTextContent("00000000-0000-7000-8000-00000000b003");
    expect(fila).not.toHaveTextContent(/Simple|Con análisis/);
  });

  it("HU14562 AC43: la cabecera ofrece la acción «Fuentes obligatorias»", async () => {
    conEmpresas();
    montar();
    expect(await screen.findByRole("link", { name: "Fuentes obligatorias" })).toHaveAttribute("href", "/backoffice/fuentes-obligatorias");
  });
});

describe("HU14562 AC43 — rediseño de F-08 con el kit", () => {
  it("cabecera «Lotes por compañía» con la banda de contexto del backoffice", async () => {
    conEmpresas();
    montar();
    expect(await screen.findByRole("heading", { level: 1, name: "Lotes por compañía" })).toBeInTheDocument();
    expect(screen.getByText("Revisa lotes con errores, reprocesa consultas y llena datos a mano")).toBeInTheDocument();
    expect(screen.getByRole("note", { name: "Contexto del backoffice" })).toHaveTextContent(
      "Backoffice FLIT · lo que haces aquí no lo ve la compañía como intervención manual",
    );
  });

  it("la tarjeta de lotes dice la compañía elegida y el total de lotes del API", async () => {
    conEmpresas({ "GET /backoffice/diagnosticos": () => Response.json({ ...listaLotes([lote(1), lote(2)]), total: 5 }) });
    montar();
    await elegir("Transportes", /Transportes A SAS/);
    const tarjeta = await screen.findByRole("region", { name: "Lotes" });
    expect(await within(tarjeta).findByRole("heading", { level: 2, name: "Lotes" })).toBeInTheDocument();
    expect(within(tarjeta).getByText("Transportes A SAS · 5 lotes")).toBeInTheDocument();
  });

  it("con la compañía solo en la URL dice el total sin inventar el nombre", async () => {
    conEmpresas({ "GET /backoffice/diagnosticos": () => Response.json({ ...listaLotes([lote(1)]), total: 1 }) });
    montar({ empresa: EMPRESA_A });
    const tarjeta = await screen.findByRole("region", { name: "Lotes" });
    expect(await within(tarjeta).findByText("1 lote")).toBeInTheDocument();
  });
});


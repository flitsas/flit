import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GridLiquidaciones } from "@/components/liquidaciones/GridLiquidaciones";
import { CONTADORES_CERO, errorRegla, fetchPorRuta, item, lote, resumen } from "./fixtures";

// Uso de ejemplo: <GridLiquidaciones puedeCrear bloqueo={null} /> en /liquidaciones. Lista las
// liquidaciones por placa y masivas juntas (pestañas «Todas» —con columna Tipo—, «Masivas» y «Por placa»), con filtros en
// la URL, y encima las tarjetas «Liquidar placa» (solo la placa, F2a-CF1) y «Carga masiva».

const push = vi.fn();
const replace = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones",
  useRouter: () => ({ replace, push, refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

const MASIVA = resumen();
const INDIVIDUAL = resumen({
  id: "0193cccc-0000-7000-8000-000000000002",
  tipo: "INDIVIDUAL",
  estado: "COMPLETADO",
  totalItems: 1,
  contadores: { ...CONTADORES_CERO, exitosas: 1, conDeuda: 1 },
  nombreArchivo: null,
  placa: "ABC123",
});

const pagina = (items: unknown[], total = items.length) => ({ items, total, page: 1, pageSize: 10 });

function render_(props: Partial<Parameters<typeof GridLiquidaciones>[0]> = {}) {
  return render(<GridLiquidaciones puedeCrear bloqueo={null} {...props} />);
}

beforeEach(() => {
  push.mockReset();
  replace.mockReset();
  busqueda = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

describe("GridLiquidaciones — grid mixto (F3-CF6) y aislamiento (F2a-CF9/CF10)", () => {
  it("«Todas» por defecto: individuales y masivas juntas con la columna Tipo y el chip de cada una", async () => {
    const fetchMock = fetchPorRuta([[/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([MASIVA, INDIVIDUAL]))]]);
    vi.stubGlobal("fetch", fetchMock);

    render_();

    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    expect(screen.getByRole("tab", { name: "Todas" })).toHaveAttribute("aria-selected", "true");
    expect(within(tabla).getByRole("columnheader", { name: "Tipo" })).toBeInTheDocument();
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas).toHaveLength(2);
    expect(within(filas[0]).getByText("Masiva")).toBeInTheDocument();
    expect(within(filas[0]).getByText("En proceso")).toBeInTheDocument();
    expect(within(filas[0]).getByText("3/10")).toBeInTheDocument();
    expect(within(filas[1]).getByText("Individual")).toBeInTheDocument();
    // La individual muestra el resultado de su única placa.
    expect(within(filas[1]).getByText("Con deuda")).toBeInTheDocument();
    // Los chips de la tabla no son regiones vivas.
    expect(within(tabla).queryAllByRole("status")).toHaveLength(0);
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://app");
    expect(url.searchParams.has("tipo")).toBe(false);
  });

  it("«Masivas» pide tipo=MASIVA, quita la columna Tipo y lo guarda en la URL", async () => {
    const fetchMock = fetchPorRuta([[/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([MASIVA]))]]);
    vi.stubGlobal("fetch", fetchMock);
    render_();
    await screen.findByRole("table", { name: "Liquidaciones" });

    fireEvent.click(screen.getByRole("tab", { name: "Masivas" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const url = new URL(String(fetchMock.mock.calls[1][0]), "http://app");
    expect(url.searchParams.get("tipo")).toBe("MASIVA");
    expect(url.searchParams.get("page")).toBe("1");
    expect(replace).toHaveBeenCalledWith(expect.stringContaining("tipo=MASIVA"), { scroll: false });
    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    expect(within(tabla).queryByRole("columnheader", { name: "Tipo" })).toBeNull();
  });

  it("«Por placa» filtra tipo=INDIVIDUAL desde la URL", async () => {
    busqueda = new URLSearchParams("tipo=INDIVIDUAL");
    const fetchMock = fetchPorRuta([[/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([INDIVIDUAL]))]]);
    vi.stubGlobal("fetch", fetchMock);

    render_();

    expect(await screen.findByRole("tab", { name: "Por placa" })).toHaveAttribute("aria-selected", "true");
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://app");
    expect(url.searchParams.get("tipo")).toBe("INDIVIDUAL");
  });

  it("propaga la compañía elegida (?empresa=) a la API y a los enlaces; sin columna Compañía (no hay vista global)", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = fetchPorRuta([[/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([MASIVA]))]]);
    vi.stubGlobal("fetch", fetchMock);

    render_({ esSuperAdmin: true });

    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://app");
    expect(url.searchParams.get("empresa")).toBe("e-9");
    expect(within(tabla).queryByRole("columnheader", { name: "Compañía" })).toBeNull();
    expect(within(tabla).queryByText("Compañía Demo")).toBeNull();
    expect(within(tabla).getByRole("link", { name: /0193AAAA/ })).toHaveAttribute("href", `/liquidaciones/${MASIVA.id}?empresa=e-9`);
  });
});

describe("GridLiquidaciones — cuatro estados", () => {
  it.each(["EMPRESA_SOLO_SUPER_ADMIN", "EMPRESA_NO_PERMITIDA"])(
    "un 403 %s (contrato 1.1.0) se pinta en la tabla con su literal y no lleva a la 403",
    async (code) => {
      vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json({ code, error: "detalle interno" }, { status: 403 })]]));
      render_();
      expect(await screen.findByText("Solo el Super Admin puede elegir la compañía.")).toBeInTheDocument();
      expect(replace).not.toHaveBeenCalledWith(expect.stringMatching(/^\/403/));
      expect(document.body).not.toHaveTextContent("detalle interno");
    },
  );

  it("otro 403 con código (PRODUCTO_NO_HABILITADO) sigue llevando a la 403", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "x" }, { status: 403 })]]));
    render_();
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
  });

  it("cargando: loader de la tabla, con las tarjetas usables", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));
    render_();
    expect(screen.getByRole("status", { name: "Cargando liquidaciones…" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Placa" })).toBeEnabled();
  });

  it("vacío sin datos y vacío por filtro son distintos", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([]))]]));
    const { unmount } = render_();
    expect(await screen.findByText("Aún no hay liquidaciones")).toBeInTheDocument();
    expect(screen.getByText("Liquida una placa o sube un archivo para empezar.")).toBeInTheDocument();
    unmount();

    busqueda = new URLSearchParams("placa=XYZ");
    render_();
    expect(await screen.findByText("Ninguna liquidación coincide con los filtros")).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "Limpiar filtros" })[0]);
    expect(replace).toHaveBeenCalledWith("/liquidaciones", { scroll: false });
  });

  it("error: texto fijo y Reintentar vuelve a pedir", async () => {
    let llamadas = 0;
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([[/liquidaciones\?/, () => (++llamadas === 1 ? new Response("boom", { status: 500 }) : Response.json(pagina([MASIVA])))]]),
    );
    render_();
    expect(await screen.findByText("No se pudieron cargar las liquidaciones")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table", { name: "Liquidaciones" })).toBeInTheDocument();
  });
});

describe("GridLiquidaciones — liquidar por placa (F2a-CF1: solo la placa) y permisos", () => {
  it("la tarjeta pide solo la placa y navega al detalle de la placa con el 201", async () => {
    const fetchMock = fetchPorRuta([
      [/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([]))],
      [
        /^POST \/api\/v1\/impuestos\/liquidaciones\/individual/,
        () => Response.json({ ...lote({ id: "L-1", tipo: "INDIVIDUAL", totalItems: 1 }), items: [item({ id: "I-1", liquidacionId: "L-1" })] }, { status: 201 }),
      ],
    ]);
    vi.stubGlobal("fetch", fetchMock);
    render_();

    const formulario = screen.getByRole("form", { name: "Liquidar placa" });
    expect(within(formulario).getAllByRole("textbox")).toHaveLength(1);
    fireEvent.change(within(formulario).getByRole("textbox", { name: "Placa" }), { target: { value: " abc-123 " } });
    fireEvent.click(within(formulario).getByRole("button", { name: "Liquidar placa" }));

    await waitFor(() => expect(push).toHaveBeenCalledWith("/liquidaciones/L-1/items/I-1"));
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === "POST");
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({ placa: "ABC123" });
  });

  it("placa inválida: el literal del contrato junto al campo, con aria-invalid, sin llamar a la API", async () => {
    const fetchMock = fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([]))]]);
    vi.stubGlobal("fetch", fetchMock);
    render_();

    const campo = screen.getByRole("textbox", { name: "Placa" });
    fireEvent.change(campo, { target: { value: "A1" } });
    fireEvent.click(screen.getByRole("button", { name: "Liquidar placa" }));

    expect(await screen.findByText("Escribe una placa válida: de 4 a 10 letras o números.")).toBeInTheDocument();
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(campo.getAttribute("aria-describedby")).toContain(screen.getByText("Escribe una placa válida: de 4 a 10 letras o números.").id);
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
    expect(campo).toHaveValue("A1");
  });

  it("409 APROVISIONAMIENTO_NO_LISTO: aviso con el literal del contrato, sin perder lo digitado", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [/^GET .*liquidaciones\?/, () => Response.json(pagina([]))],
        [
          /^POST .*individual/,
          () => errorRegla(409, "APROVISIONAMIENTO_NO_LISTO", "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista."),
        ],
      ]),
    );
    render_();
    fireEvent.change(screen.getByRole("textbox", { name: "Placa" }), { target: { value: "ABC123" } });
    fireEvent.click(screen.getByRole("button", { name: "Liquidar placa" }));

    expect(await screen.findByText("La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Placa" })).toHaveValue("ABC123");
  });

  it("sin impuestos.liquidaciones.crear no hay tarjetas de creación en el DOM", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([MASIVA]))]]));
    render_({ puedeCrear: false });
    await screen.findByRole("table", { name: "Liquidaciones" });
    expect(screen.queryByRole("textbox", { name: "Placa" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Carga masiva" })).toBeNull();
  });

  it("con el producto no listo o suspendido, las acciones quedan deshabilitadas con el motivo visible y asociado", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([]))]]));
    render_({ bloqueo: "Impuestos está suspendido para la compañía: solo lectura." });

    const liquidar = screen.getByRole("button", { name: "Liquidar placa" });
    expect(liquidar).toBeDisabled();
    const motivo = screen.getAllByText("Impuestos está suspendido para la compañía: solo lectura.")[0];
    expect(liquidar.getAttribute("aria-describedby")).toContain(motivo.id);
    expect(screen.getByRole("button", { name: "Carga masiva" })).toBeDisabled();
  });
});

describe("GridLiquidaciones — contrato 1.1.0 (delta 4: itemId y estadoItem de la individual)", () => {
  const INDIVIDUAL_110 = resumen({
    id: "0193dddd-0000-7000-8000-000000000003",
    tipo: "INDIVIDUAL",
    estado: "COMPLETADO_CON_NOVEDADES",
    totalItems: 1,
    contadores: { ...CONTADORES_CERO, incierto: 1 },
    nombreArchivo: null,
    placa: "ABC123",
    itemId: "0193eeee-0000-7000-8000-000000000004",
    estadoItem: "INCIERTO_REQUIERE_DECISION",
  });

  it("la individual enlaza directo al detalle de su placa y muestra el chip de estadoItem; la masiva sigue con el lote", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    vi.stubGlobal("fetch", fetchPorRuta([[/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([MASIVA, INDIVIDUAL_110]))]]));

    render_({ esSuperAdmin: true });

    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    const [filaMasiva, filaIndividual] = within(tabla).getAllByRole("row").slice(1);
    expect(within(filaIndividual).getByRole("link")).toHaveAttribute(
      "href",
      `/liquidaciones/${INDIVIDUAL_110.id}/items/${INDIVIDUAL_110.itemId}?empresa=e-9`,
    );
    // El chip sale de estadoItem (no de los contadores, que solo dirían «Inciertas»).
    expect(within(filaIndividual).getByText("Incierto — requiere decisión")).toBeInTheDocument();
    expect(within(filaMasiva).getByRole("link")).toHaveAttribute("href", `/liquidaciones/${MASIVA.id}?empresa=e-9`);
    expect(within(filaMasiva).getByText("En proceso")).toBeInTheDocument();
  });

  it("una individual EXITOSO muestra el resultado de su placa («Con deuda») con estadoItem", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [
          /liquidaciones\?/,
          () =>
            Response.json(
              pagina([{ ...INDIVIDUAL_110, estado: "COMPLETADO", estadoItem: "EXITOSO", contadores: { ...CONTADORES_CERO, exitosas: 1, conDeuda: 1 } }]),
            ),
        ],
      ]),
    );
    render_();
    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    expect(within(tabla).getByText("Con deuda")).toBeInTheDocument();
  });

  it("una individual en ERROR con placaNoInscrita muestra «Con novedad»; sin el campo sigue «Con error»", async () => {
    const NO_INSCRITA = { ...INDIVIDUAL_110, id: "0193ffff-0000-7000-8000-000000000005", placa: "XYZ987", estadoItem: "ERROR", placaNoInscrita: true, contadores: { ...CONTADORES_CERO, error: 1 } };
    const CON_ERROR = { ...INDIVIDUAL_110, estadoItem: "ERROR", contadores: { ...CONTADORES_CERO, error: 1 } };
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([NO_INSCRITA, CON_ERROR]))]]));
    render_();
    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    const [filaNoInscrita, filaError] = within(tabla).getAllByRole("row").slice(1);
    expect(within(filaNoInscrita).getByText("Con novedad")).toBeInTheDocument();
    expect(within(filaNoInscrita).queryByText("Con error")).toBeNull();
    expect(within(filaError).getByText("Con error")).toBeInTheDocument();
  });

  it("una individual sin itemId (respuesta vieja) sigue enlazando al lote, que redirige", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([INDIVIDUAL]))]]));
    render_();
    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    expect(within(tabla).getByRole("link")).toHaveAttribute("href", `/liquidaciones/${INDIVIDUAL.id}`);
  });
});

describe("GridLiquidaciones — Super Admin y ?empresa (delta 2)", () => {
  it("el Super Admin sin compañía no pide el grid: «Elige una compañía.» y las acciones de creación deshabilitadas con el motivo", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    render_({ esSuperAdmin: true });

    expect(screen.getAllByText("Elige una compañía.").length).toBeGreaterThan(0);
    expect(screen.queryByRole("status", { name: "Cargando liquidaciones…" })).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
    const liquidar = screen.getByRole("button", { name: "Liquidar placa" });
    expect(liquidar).toBeDisabled();
    const motivo = screen.getAllByText("Elige una compañía.")[0];
    expect(liquidar.getAttribute("aria-describedby")).toContain(motivo.id);
    expect(screen.getByRole("button", { name: "Carga masiva" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Descargar plantilla" })).toBeDisabled();
    expect(replace).not.toHaveBeenCalled();
  });

  it("quien no es Super Admin, sin ?empresa, pide el grid de su compañía", async () => {
    const fetchMock = fetchPorRuta([[/liquidaciones\?/, () => Response.json(pagina([MASIVA]))]]);
    vi.stubGlobal("fetch", fetchMock);
    render_({ esSuperAdmin: false });
    await screen.findByRole("table", { name: "Liquidaciones" });
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://app");
    expect(url.searchParams.has("empresa")).toBe(false);
  });

  it.each([
    [400, "FILTRO_INVALIDO", "Un filtro no es válido."],
    [400, "PAGINACION_INVALIDA", "La paginación no es válida."],
    [400, "RANGO_INVALIDO", "El rango de fechas no es válido."],
    [400, "EMPRESA_REQUERIDA", "Elige una compañía."],
    [404, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos."],
  ])("un %i %s del grid se pinta en línea con el literal del contrato, sin ir a la 403", async (status, code, literal) => {
    busqueda = new URLSearchParams("empresa=e-9&desde=2026-12-01&hasta=2026-01-01");
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\?/, () => errorRegla(status, code, "detalle interno")]]));
    render_({ esSuperAdmin: true });
    expect(await screen.findByText("No se pudieron cargar las liquidaciones")).toBeInTheDocument();
    expect(screen.getByText(literal)).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("detalle interno");
    expect(replace).not.toHaveBeenCalledWith(expect.stringMatching(/^\/403/));
  });
});

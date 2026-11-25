import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { GridLiquidaciones } from "@/components/liquidaciones/GridLiquidaciones";
import { CONTADORES_CERO, errorRegla, fetchPorRuta, item, lote, resumen } from "./fixtures";

// Uso de ejemplo: <GridLiquidaciones puedeCrear mostrarCompania={false} bloqueo={null} /> en /liquidaciones. Lista las
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
  return render(<GridLiquidaciones puedeCrear mostrarCompania={false} bloqueo={null} {...props} />);
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

  it("propaga la compañía elegida (?empresa=) a la API y a los enlaces; la columna Compañía solo para el Super Admin", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = fetchPorRuta([[/^GET \/api\/v1\/impuestos\/liquidaciones\?/, () => Response.json(pagina([MASIVA]))]]);
    vi.stubGlobal("fetch", fetchMock);

    const { unmount } = render_({ mostrarCompania: true });

    const tabla = await screen.findByRole("table", { name: "Liquidaciones" });
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://app");
    expect(url.searchParams.get("empresa")).toBe("e-9");
    expect(within(tabla).getByRole("columnheader", { name: "Compañía" })).toBeInTheDocument();
    expect(within(tabla).getByText("Compañía Demo")).toBeInTheDocument();
    expect(within(tabla).getByRole("link", { name: /0193AAAA/ })).toHaveAttribute("href", `/liquidaciones/${MASIVA.id}?empresa=e-9`);
    unmount();

    render_({ mostrarCompania: false });
    const otra = await screen.findByRole("table", { name: "Liquidaciones" });
    expect(within(otra).queryByRole("columnheader", { name: "Compañía" })).toBeNull();
  });
});

describe("GridLiquidaciones — cuatro estados", () => {
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

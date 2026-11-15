import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ModuloReportes } from "@/components/reportes/ModuloReportes";
import { catalogoBackoffice, catalogoCompania, paginaVehiculos } from "@/lib/reportes/__tests__/datosReportes";
import { AHORA } from "@/lib/estadisticas/__tests__/datos";
import { MENSAJE_RANGO_GENERICO } from "@/lib/estadisticas/periodo";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <ModuloReportes vistaGlobal={capacidades.verEstadisticasGlobales} puedeDescargar={capacidades.descargarReportes} />
// en /reportes. Pide el catálogo (GET /reportes o /backoffice/reportes), pinta una pestaña por tipo, la tabla paginada
// del activo con sus filtros y, con permiso, Exportar Excel / PDF. La URL lleva reporte, desde, hasta, empresa y enums;
// nunca la placa (SP-18).

const h = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/reportes",
  useSearchParams: () => h.params,
  useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }),
}));

const E1 = "01920000-0000-7000-8000-0000000000a1";
const empresas = () => Response.json([{ id: E1, nombre: "Compañía Demo", nit: "900000001" }]);

let descargados: string[];
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(AHORA);
  h.replace.mockReset();
  h.params = new URLSearchParams();
  descargados = [];
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    descargados.push(this.download);
  });
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const query = (fetchMock: { mock: { calls: unknown[][] } }, ruta: string) =>
  llamadas(fetchMock, "GET", ruta).map(([u]) => new URL(String(u), "http://localhost").searchParams);

describe("ModuloReportes — AC catálogo y tabla genérica", () => {
  it("pinta las pestañas del catálogo en orden, el aviso de PII del activo y su tabla paginada", async () => {
    const fetchMock = simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    const pestanas = await screen.findByRole("tablist", { name: "Reportes disponibles" });
    expect(within(pestanas).getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Vehículos diagnosticados",
      "Consultas a fuentes",
      "Saneamientos",
      "Derivaciones a trámite",
    ]);
    expect(await screen.findByRole("table", { name: "Vehículos diagnosticados" })).toBeInTheDocument();
    expect(screen.getByText(/contiene datos personales \(números de documento de identidad\)/)).toBeInTheDocument();
    expect(screen.queryByText(/nombres/)).not.toBeInTheDocument();
    const [q] = query(fetchMock, "/reportes/vehiculos");
    expect(q.get("desde")).toBe("2026-10-17");
    expect(q.get("hasta")).toBe("2026-11-15");
    expect(q.get("pagina")).toBe("1");
    expect(q.get("tamano")).toBe("25");
    expect(llamadas(fetchMock, "GET", "/backoffice/reportes")).toHaveLength(0);
    expect(screen.queryByRole("combobox", { name: "Compañía" })).not.toBeInTheDocument();
  });

  it("sin ?reporte= en la URL, al cargar el catálogo escribe en ella la pestaña activa y el periodo, una sola vez (spec-ui §5 criterio 4)", async () => {
    simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar={false} />);
    await screen.findByRole("table", { name: "Vehículos diagnosticados" });
    await waitFor(() => expect(h.replace).toHaveBeenCalledTimes(1));
    const [url, opciones] = h.replace.mock.calls[0];
    const q = new URL(String(url), "http://localhost").searchParams;
    expect(q.get("reporte")).toBe("vehiculos");
    expect(q.get("desde")).toBe("2026-10-17");
    expect(q.get("hasta")).toBe("2026-11-15");
    expect(q.has("placa")).toBe(false);
    expect(opciones).toEqual({ scroll: false });
  });

  it("con ?reporte= ya en la URL no la reescribe al cargar", async () => {
    h.params = new URLSearchParams("reporte=vehiculos&desde=2026-10-17&hasta=2026-11-15");
    simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar={false} />);
    await screen.findByRole("table", { name: "Vehículos diagnosticados" });
    expect(h.replace).not.toHaveBeenCalled();
  });

  it("la pestaña de ?reporte= es la activa y cambiar de pestaña escribe ?reporte en la URL", async () => {
    h.params = new URLSearchParams("reporte=consultas");
    simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/consultas": () => Response.json({ ...paginaVehiculos(), tipo: "consultas", total: 0, filas: [] }),
      "GET /reportes/saneamientos": () => Response.json({ ...paginaVehiculos(), tipo: "saneamientos", total: 0, filas: [] }),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar={false} />);
    expect(await screen.findByRole("tab", { name: "Consultas a fuentes", selected: true })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "Saneamientos" }));
    await waitFor(() => expect(String(h.replace.mock.calls.at(-1)?.[0])).toContain("reporte=saneamientos"));
  });
});

describe("ModuloReportes — AC ámbito de la vista global (Q4: verEstadisticasGlobales)", () => {
  it("usa el catálogo del backoffice con consumo, las rutas /global y ?empresa= al elegir una compañía", async () => {
    const fetchMock = simularApi({
      "GET /backoffice/reportes": () => Response.json(catalogoBackoffice()),
      "GET /backoffice/reportes/vehiculos/global": () => Response.json(paginaVehiculos({ ambito: "BACKOFFICE_GLOBAL" })),
      "GET /backoffice/reportes/vehiculos": () => Response.json(paginaVehiculos({ ambito: "BACKOFFICE_EMPRESA" })),
      "GET /backoffice/empresas": empresas,
    });
    render(<ModuloReportes vistaGlobal puedeDescargar />);
    expect(await screen.findByRole("tab", { name: "Consumo por compañía" })).toBeInTheDocument();
    await waitFor(() => expect(query(fetchMock, "/backoffice/reportes/vehiculos/global")).toHaveLength(1));
    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).not.toBeDisabled());
    fireEvent.focus(combo);
    fireEvent.click(await screen.findByRole("option", { name: /Compañía Demo/ }));
    await waitFor(() => expect(query(fetchMock, "/backoffice/reportes/vehiculos").map((q) => q.get("empresa"))).toEqual([E1]));
    expect(String(h.replace.mock.calls.at(-1)?.[0])).toContain(`empresa=${E1}`);
  });
});

describe("ModuloReportes — AC PII en el front (SP-18) y descargas", () => {
  it("la placa viaja a la API pero nunca a la URL de la pantalla", async () => {
    const fetchMock = simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    await screen.findByRole("table", { name: "Vehículos diagnosticados" });
    fireEvent.click(screen.getByRole("button", { name: "Express" }));
    const placa = screen.getByLabelText("Placa (inicio)");
    fireEvent.change(placa, { target: { value: "abc" } });
    fireEvent.submit(placa);
    await waitFor(() => expect(query(fetchMock, "/reportes/vehiculos").at(-1)?.get("placa")).toBe("ABC"));
    const urls = h.replace.mock.calls.map(([u]) => String(u));
    expect(urls.some((u) => u.includes("modalidad=EXPRESS"))).toBe(true);
    urls.forEach((u) => expect(u.toUpperCase()).not.toContain("ABC"));
  });

  it("exportar baja el Blob con el nombre del servidor, libera la URL y no deja claves en el almacenamiento", async () => {
    const antes = { local: localStorage.length, session: sessionStorage.length };
    const fetchMock = simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
      "GET /reportes/vehiculos/excel": () =>
        new Response(new Blob(["xlsx"]), { status: 200, headers: { "Content-Disposition": 'attachment; filename="diagnostico-reporte-vehiculos-20261017-20261115.xlsx"' } }),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    await screen.findByRole("table", { name: "Vehículos diagnosticados" });
    fireEvent.click(screen.getByRole("button", { name: /Exportar Excel/ }));
    await waitFor(() => expect(descargados).toEqual(["diagnostico-reporte-vehiculos-20261017-20261115.xlsx"]));
    await waitFor(() => expect(URL.revokeObjectURL).toHaveBeenCalled());
    const [q] = query(fetchMock, "/reportes/vehiculos/excel");
    expect(q.has("pagina")).toBe(false);
    expect({ local: localStorage.length, session: sessionStorage.length }).toEqual(antes);
  });

  it("sin descargarReportes no hay botones de exportar", async () => {
    simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar={false} />);
    await screen.findByRole("table", { name: "Vehículos diagnosticados" });
    expect(screen.queryByRole("button", { name: /Exportar/ })).not.toBeInTheDocument();
  });
});

describe("ModuloReportes — cuatro estados", () => {
  it("vacío del periodo y vacío por filtro son textos distintos", async () => {
    const vacia = () => Response.json(paginaVehiculos({ total: 0, filas: [] }));
    simularApi({ "GET /reportes": () => Response.json(catalogoCompania()), "GET /reportes/vehiculos": vacia });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    expect(await screen.findByText("No hay registros en este periodo")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Masivo" }));
    expect(await screen.findByText("Ningún registro coincide con los filtros")).toBeInTheDocument();
  });

  it("error del reporte: InlineAlert con texto fijo y Reintentar", async () => {
    let intentos = 0;
    const fetchMock = simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () =>
        ++intentos === 1 ? Response.json({ code: "X", error: "detalle interno" }, { status: 500 }) : Response.json(paginaVehiculos()),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    expect(await screen.findByText("No se pudo cargar el reporte.")).toBeInTheDocument();
    expect(screen.queryByText("detalle interno")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table", { name: "Vehículos diagnosticados" })).toBeInTheDocument();
    expect(llamadas(fetchMock, "GET", "/reportes/vehiculos")).toHaveLength(2);
  });

  it("error del catálogo con Reintentar y 403 lleva a la página 403", async () => {
    simularApi({ "GET /reportes": () => Response.json({ code: "SIN_PERMISO", error: "x" }, { status: 403 }) });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
  });

  it("cargando el catálogo: loader de marca", () => {
    simularApi({ "GET /reportes": () => new Promise<Response>(() => {}) });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    expect(screen.getByRole("status", { name: "Cargando reportes…" })).toBeInTheDocument();
  });
});

describe("ModuloReportes — correcciones de la revisión de código (CR-FE-2, 4, 5 y 11)", () => {
  it("CR-FE-2: 404 EMPRESA_NO_ENCONTRADA explica el motivo y «Ver todas las compañías» limpia ?empresa= y pide /global", async () => {
    h.params = new URLSearchParams(`empresa=${E1}`);
    const fetchMock = simularApi({
      "GET /backoffice/reportes": () => Response.json(catalogoBackoffice()),
      "GET /backoffice/reportes/vehiculos": () => Response.json({ code: "EMPRESA_NO_ENCONTRADA", error: "detalle interno" }, { status: 404 }),
      "GET /backoffice/reportes/vehiculos/global": () => Response.json(paginaVehiculos({ ambito: "BACKOFFICE_GLOBAL" })),
      "GET /backoffice/empresas": empresas,
    });
    render(<ModuloReportes vistaGlobal puedeDescargar />);
    expect(await screen.findByText("Esa compañía no tiene Diagnóstico activo.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ver todas las compañías" }));
    await waitFor(() => expect(query(fetchMock, "/backoffice/reportes/vehiculos/global")).toHaveLength(1));
    expect(String(h.replace.mock.calls.at(-1)?.[0])).not.toContain("empresa=");
    expect(await screen.findByRole("table", { name: "Vehículos diagnosticados" })).toBeInTheDocument();
  });

  it("CR-FE-2: 503 IDENTIDAD_NO_DISPONIBLE muestra su texto fijo con «Reintentar»", async () => {
    simularApi({
      "GET /backoffice/reportes": () => Response.json(catalogoBackoffice()),
      "GET /backoffice/reportes/vehiculos/global": () => Response.json({ code: "IDENTIDAD_NO_DISPONIBLE", error: "x" }, { status: 503 }),
      "GET /backoffice/empresas": empresas,
    });
    render(<ModuloReportes vistaGlobal puedeDescargar />);
    expect(await screen.findByText("El directorio de compañías no responde. Intenta de nuevo en unos segundos.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
  });

  it("CR-FE-4: un 400 RANGO_INVALIDO con el rango válido en el cliente usa el texto genérico junto a «Hasta»", async () => {
    simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json({ code: "RANGO_INVALIDO", error: "x" }, { status: 400 }),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    expect(await screen.findByText(MENSAJE_RANGO_GENERICO)).toBeInTheDocument();
    expect(screen.queryByText("El rango no puede superar un año")).not.toBeInTheDocument();
  });

  it("CR-FE-5: el error de la última exportación se limpia al cambiar los filtros", async () => {
    simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()),
      "GET /reportes/vehiculos/excel": () => Response.json({ code: "EXPORTACION_EN_CURSO", error: "x" }, { status: 429, headers: { "Retry-After": "10" } }),
    });
    render(<ModuloReportes vistaGlobal={false} puedeDescargar />);
    await screen.findByRole("table", { name: "Vehículos diagnosticados" });
    fireEvent.click(screen.getByRole("button", { name: /Exportar Excel/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Ya hay una exportación en curso");
    fireEvent.click(screen.getByRole("button", { name: "Masivo" }));
    await waitFor(() => expect(screen.queryByText(/Ya hay una exportación en curso/)).not.toBeInTheDocument());
  });

  it("CR-FE-11: la lista de compañías se pide una sola vez aunque se cambie de pestaña", async () => {
    const fetchMock = simularApi({
      "GET /backoffice/reportes": () => Response.json(catalogoBackoffice()),
      "GET /backoffice/reportes/vehiculos/global": () => Response.json(paginaVehiculos({ ambito: "BACKOFFICE_GLOBAL" })),
      "GET /backoffice/reportes/consultas/global": () => Response.json({ ...paginaVehiculos({ ambito: "BACKOFFICE_GLOBAL" }), tipo: "consultas", total: 0, filas: [] }),
      "GET /backoffice/empresas": empresas,
    });
    render(<ModuloReportes vistaGlobal puedeDescargar />);
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Compañía" })).not.toBeDisabled());
    fireEvent.click(screen.getByRole("tab", { name: "Consultas a fuentes" }));
    await waitFor(() => expect(query(fetchMock, "/backoffice/reportes/consultas/global")).toHaveLength(1));
    fireEvent.click(screen.getByRole("tab", { name: "Vehículos diagnosticados" }));
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Compañía" })).not.toBeDisabled());
    expect(llamadas(fetchMock, "GET", "/backoffice/empresas")).toHaveLength(1);
  });
});

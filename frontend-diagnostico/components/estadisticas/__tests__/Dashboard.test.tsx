import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Dashboard } from "@/components/estadisticas/Dashboard";
import { AHORA, estadisticas } from "@/lib/estadisticas/__tests__/datos";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <Dashboard vistaGlobal={capacidades.verEstadisticasGlobales} puedeCrear={capacidades.crearDiagnosticos}
// empresaNombre={sesion.empresa.nombre} /> en la raíz. Lee desde/hasta/empresa de la URL (por defecto los últimos 30
// días), pide GET /estadisticas (compañía) o /backoffice/estadisticas/global | ?empresa= (vista global) y pinta KPIs,
// serie, veredictos, tipo de análisis, fuentes, saneamientos, derivaciones y, solo en global, el top de compañías.

const h = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => h.params,
  useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }),
}));

const E1 = "01920000-0000-7000-8000-0000000000a1";
const empresas = () => Response.json([{ id: E1, nombre: "Compañía Demo", nit: "900000001" }]);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(AHORA);
  h.replace.mockReset();
  h.push.mockReset();
  h.params = new URLSearchParams();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const query = (fetchMock: { mock: { calls: unknown[][] } }, ruta: string) =>
  llamadas(fetchMock, "GET", ruta).map(([u]) => Object.fromEntries(new URL(String(u), "http://localhost").searchParams));

describe("Dashboard — AC vista de compañía", () => {
  it("pide GET /estadisticas con los últimos 30 días y pinta todas las secciones sin selector ni top", async () => {
    const fetchMock = simularApi({ "GET /estadisticas": () => Response.json(estadisticas()) });
    render(<Dashboard vistaGlobal={false} puedeCrear empresaNombre="Compañía Demo" />);

    expect(await screen.findByText("1.702 únicos")).toBeInTheDocument();
    expect(query(fetchMock, "/estadisticas")).toEqual([{ desde: "2026-10-17", hasta: "2026-11-15" }]);

    // KPIs (TarjetaMetrica) con cifras es-CO y su pie.
    expect(screen.getByRole("link", { name: /^Diagnósticos\s*42/ })).toHaveAttribute("href", "/diagnosticos?desde=2026-10-17&hasta=2026-11-15");
    expect(screen.getByText("30 express · 12 masivos")).toBeInTheDocument();
    expect(screen.getByText("1.702 únicos")).toBeInTheDocument();
    expect(screen.getByText("10.980")).toBeInTheDocument();
    expect(screen.getByText("97,2 % de éxito")).toBeInTheDocument();
    expect(screen.getByText("2,3 s")).toBeInTheDocument();

    // Secciones con figura y tabla alternativa.
    for (const nombre of ["Uso en el tiempo", "Veredictos", "Tipo de análisis", "Saneamientos", "Trámites"]) {
      const figura = screen.getByRole("figure", { name: nombre });
      expect(within(figura).getByRole("table")).toBeInTheDocument();
    }
    const fuentes = screen.getByRole("table", { name: "Salud de las fuentes" });
    expect(within(fuentes).getByText("RUNT vehículo")).toBeInTheDocument();
    expect(within(fuentes).getByText("SIMIT vehículo")).toBeInTheDocument();
    expect(within(fuentes).getByText("98,9 %")).toBeInTheDocument();

    expect(screen.getByText("Resumen de diagnósticos de Compañía Demo")).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Compañía" })).not.toBeInTheDocument();
    expect(screen.queryByText("Compañías con más uso")).not.toBeInTheDocument();
    expect(llamadas(fetchMock, "GET", "/backoffice/empresas")).toHaveLength(0);
  });

  it("respeta desde y hasta de la URL y no envía ?empresa= aunque venga en la URL", async () => {
    h.params = new URLSearchParams(`desde=2026-11-01&hasta=2026-11-10&empresa=${E1}`);
    const fetchMock = simularApi({ "GET /estadisticas": () => Response.json(estadisticas()) });
    render(<Dashboard vistaGlobal={false} puedeCrear={false} />);
    await screen.findByText("1.702 únicos");
    expect(query(fetchMock, "/estadisticas")).toEqual([{ desde: "2026-11-01", hasta: "2026-11-10" }]);
  });
});

describe("Dashboard — AC vista global y selector", () => {
  it("abre en «Todas las compañías» con el top; elegir una compañía pide ?empresa= y la escribe en la URL", async () => {
    const fetchMock = simularApi({
      "GET /backoffice/estadisticas/global": () =>
        Response.json(
          estadisticas({
            ambito: { tipo: "GLOBAL", empresa_id: null, empresa_nombre: null },
            top_empresas: [{ empresa_id: E1, empresa_nombre: "Compañía Demo", diagnosticos: 40, vehiculos: 1200 }],
          }),
        ),
      "GET /backoffice/estadisticas": () => Response.json(estadisticas({ ambito: { tipo: "EMPRESA", empresa_id: E1, empresa_nombre: "Compañía Demo" } })),
      "GET /backoffice/empresas": empresas,
    });
    render(<Dashboard vistaGlobal puedeCrear={false} />);

    expect(await screen.findByText("Compañías con más uso")).toBeInTheDocument();
    expect(query(fetchMock, "/backoffice/estadisticas/global")).toEqual([{ desde: "2026-10-17", hasta: "2026-11-15" }]);
    expect(screen.getByText("Vista global · todas las compañías")).toBeInTheDocument();
    const combo = screen.getByRole("combobox", { name: "Compañía" });
    await waitFor(() => expect(combo).not.toBeDisabled());
    expect(combo).toHaveValue("Todas las compañías");

    fireEvent.focus(combo);
    fireEvent.click(await screen.findByRole("option", { name: /Compañía Demo/ }));

    await waitFor(() => expect(query(fetchMock, "/backoffice/estadisticas")).toEqual([{ desde: "2026-10-17", hasta: "2026-11-15", empresa: E1 }]));
    const url = String(h.replace.mock.calls.at(-1)?.[0]);
    expect(url).toContain("desde=2026-10-17");
    expect(url).toContain("hasta=2026-11-15");
    expect(url).toContain(`empresa=${E1}`);
    expect(await screen.findByText("Resumen de diagnósticos de Compañía Demo")).toBeInTheDocument();
  });

  it("el botón del top elige esa compañía", async () => {
    const fetchMock = simularApi({
      "GET /backoffice/estadisticas/global": () =>
        Response.json(estadisticas({ top_empresas: [{ empresa_id: E1, empresa_nombre: "Compañía Demo", diagnosticos: 40, vehiculos: 1200 }] })),
      "GET /backoffice/estadisticas": () => Response.json(estadisticas()),
      "GET /backoffice/empresas": empresas,
    });
    render(<Dashboard vistaGlobal puedeCrear={false} />);
    fireEvent.click(await screen.findByRole("button", { name: "Ver el resumen de Compañía Demo" }));
    await waitFor(() => expect(query(fetchMock, "/backoffice/estadisticas")).toHaveLength(1));
  });
});

describe("Dashboard — AC filtros de fecha", () => {
  it("un rango de más de 366 días no se envía y explica el límite", async () => {
    const fetchMock = simularApi({ "GET /estadisticas": () => Response.json(estadisticas()) });
    render(<Dashboard vistaGlobal={false} puedeCrear={false} />);
    await screen.findByText("1.702 únicos");
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2025-01-01" } });
    expect(screen.getByText("El rango no puede superar un año")).toBeInTheDocument();
    expect(llamadas(fetchMock, "GET", "/estadisticas")).toHaveLength(1);
  });

  it("si el servidor responde 400 RANGO_INVALIDO muestra el mensaje junto a «Hasta»", async () => {
    simularApi({ "GET /estadisticas": () => Response.json({ code: "RANGO_INVALIDO", error: "x" }, { status: 400 }) });
    render(<Dashboard vistaGlobal={false} puedeCrear={false} />);
    expect(await screen.findByText("El rango no puede superar un año")).toBeInTheDocument();
    expect(screen.getByLabelText("Hasta")).toHaveAttribute("aria-invalid", "true");
  });
});

describe("Dashboard — AC estados", () => {
  it("cargando: loader de marca y tarjetas en espera", async () => {
    simularApi({ "GET /estadisticas": () => new Promise<Response>(() => {}) });
    render(<Dashboard vistaGlobal={false} puedeCrear={false} />);
    expect(screen.getByRole("status", { name: "Cargando el resumen…" })).toBeInTheDocument();
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(6);
  });

  it("vacío: EstadoVacio con «Nuevo diagnóstico» (si puede crear) y «Ver los últimos 90 días»", async () => {
    const vacio = estadisticas({
      resumen: { ...estadisticas().resumen, diagnosticos: 0, diagnosticos_express: 0, diagnosticos_masivos: 0, vehiculos: 0, vehiculos_unicos: 0, consultas: 0 },
      fuentes: [],
    });
    simularApi({ "GET /estadisticas": () => Response.json(vacio) });
    render(<Dashboard vistaGlobal={false} puedeCrear />);
    expect(await screen.findByText("Aún no hay diagnósticos en este periodo")).toBeInTheDocument();
    const nuevos = screen.getAllByRole("link", { name: /Nuevo diagnóstico/ });
    expect(nuevos).toHaveLength(2);
    nuevos.forEach((a) => expect(a).toHaveAttribute("href", "/diagnosticos/nuevo"));
    fireEvent.click(screen.getByRole("button", { name: "Ver los últimos 90 días" }));
    expect(String(h.replace.mock.calls.at(-1)?.[0])).toContain("desde=2026-08-18");
  });

  it("403: lleva a la página 403 con el código", async () => {
    simularApi({ "GET /estadisticas": () => Response.json({ code: "SIN_PERMISO", error: "x" }, { status: 403 }) });
    render(<Dashboard vistaGlobal={false} puedeCrear={false} />);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO"));
  });

  it("503: InlineAlert con Reintentar que vuelve a pedir", async () => {
    let intentos = 0;
    const fetchMock = simularApi({
      "GET /backoffice/estadisticas/global": () =>
        ++intentos === 1 ? Response.json({ code: "IDENTIDAD_NO_DISPONIBLE", error: "x" }, { status: 503 }) : Response.json(estadisticas()),
      "GET /backoffice/empresas": empresas,
    });
    render(<Dashboard vistaGlobal puedeCrear={false} />);
    const alerta = await screen.findByText("No se pudo cargar el resumen");
    expect(alerta).toBeInTheDocument();
    expect(screen.getAllByText(/Error al cargar/).length).toBeGreaterThan(0);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    });
    expect(await screen.findByText("1.702 únicos")).toBeInTheDocument();
    expect(llamadas(fetchMock, "GET", "/backoffice/estadisticas/global")).toHaveLength(2);
  });
});

describe("Dashboard — sin PII (spec-ui §4.8.3)", () => {
  it("no pinta placas ni documentos", async () => {
    simularApi({ "GET /estadisticas": () => Response.json(estadisticas()) });
    const { container } = render(<Dashboard vistaGlobal={false} puedeCrear={false} />);
    await screen.findByText("1.702 únicos");
    expect(container.textContent).not.toMatch(/\b[A-Z]{3}\d{3}\b/);
  });
});

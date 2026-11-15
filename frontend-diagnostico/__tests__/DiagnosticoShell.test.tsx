import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, renderHook, screen, waitFor, within } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import { useSondeoResumenSaneamientos } from "@/lib/backoffice/useResumenSaneamientos";

// Uso de ejemplo: <DiagnosticoShell user={usuarioDelToken} tramitesUrl="https://tramites">{pantalla}</DiagnosticoShell>
// pide GET /api/v1/diagnostico/sesion y, con la sesión, dibuja la barra de la suite con la compañía y la pantalla.

const replace = vi.fn();
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }) }));

const usuario = { email: "ana@empresa-prueba.co", tenantName: "Nombre del token", permissions: ["diagnostico.diagnosticos.ver"], roles: ["usuario_diagnostico"], isSuperAdmin: false };

const sesion = {
  usuario: { id: "00000000-0000-7000-8000-000000000001", email: "ana@empresa-prueba.co", esSuperAdmin: false },
  empresa: { id: "00000000-0000-7000-8000-0000000000e1", nombre: "Transportes Prueba SAS", tipo: "COMPANY" },
  roles: ["usuario_diagnostico"],
  permisos: ["diagnostico.diagnosticos.ver"],
  capacidades: {
    crearDiagnosticos: false, verDiagnosticos: true, descargarReportes: false, derivar: false, verConfiguracion: false,
    editarConfiguracion: false, reprocesarConsultas: false, backoffice: false, editarContrato: false, llenarConsultas: false,
    corregirDictamen: false, verAuditoria: false, verVolumetria: false,
  },
};

function montar() {
  return render(
    <DiagnosticoShell user={usuario} tramitesUrl="https://tramites.prueba">
      <p>pantalla base</p>
    </DiagnosticoShell>,
  );
}

beforeEach(() => replace.mockReset());
afterEach(() => vi.unstubAllGlobals());

describe("DiagnosticoShell", () => {
  it("muestra la compañía de GET /sesion y no tiene selector de compañía", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(sesion));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    expect(await screen.findByText("pantalla base")).toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/diagnostico/sesion");
    const barra = screen.getByRole("banner");
    expect(within(barra).getByText("Transportes Prueba SAS")).toBeInTheDocument();
    expect(within(barra).queryByText("Nombre del token")).toBeNull();
    expect(screen.queryAllByRole("combobox")).toHaveLength(0);
    expect(screen.queryAllByRole("listbox")).toHaveLength(0);
    expect(screen.queryByLabelText(/compañía|empresa/i)).toBeNull();
  });

  it("es la barra de la suite (@flit/shell) con el producto Diagnóstico", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(sesion)));

    montar();

    await screen.findByText("pantalla base");
    const barra = screen.getByRole("banner");
    expect(within(barra).getByText("Diagnóstico")).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "Productos" })).toBeInTheDocument();
    expect(within(barra).getByRole("button", { name: "Menú de usuario" })).toBeInTheDocument();
  });

  it("mientras carga la sesión muestra el loader de @flit/ui y no la pantalla", () => {
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise(() => {})));

    montar();

    const loader = screen.getByRole("status", { name: "Abriendo Diagnóstico…" });
    // UI Kit 2026 (propuesta-2): el cargador del kit (pista, línea y vehículo) en lugar del aro con el ícono.
    expect(loader).toHaveClass("flit-cargador");
    expect(screen.queryByText("pantalla base")).toBeNull();
  });

  it("con 403 PRODUCTO_NO_HABILITADO lleva a la página 403 y no muestra la pantalla base", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "x" }, { status: 403 })));

    montar();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
    expect(screen.queryByText("pantalla base")).toBeNull();
  });

  it("con 403 sin permisos de Diagnóstico va a la 403 (no al login: sin bucle)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 403 }));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/403?code=SIN_PERMISOS_DIAGNOSTICO"));
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("pantalla base")).toBeNull();
  });

  it.each([
    ["un 4xx con el mensaje del servidor en el cuerpo", () => Response.json({ code: "OTRO", error: "mensaje del servidor que no debe verse" }, { status: 422 })],
    ["un error de red", () => Promise.reject(new TypeError("mensaje del servidor que no debe verse"))],
  ])("AP-9: %s muestra el respaldo fijo, nunca el mensaje", async (_n, respuesta) => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(respuesta));

    montar();

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo abrir Diagnóstico.");
    expect(alerta).not.toHaveTextContent("mensaje del servidor que no debe verse");
  });

  it("si la API falla muestra el error con Reintentar y, al reintentar, abre la pantalla", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(Response.json({}, { status: 503 }))
      .mockResolvedValueOnce(Response.json(sesion));
    vi.stubGlobal("fetch", fetchMock);

    montar();

    // AP-9 (SF-6): reescrito. Un 5xx cae en el respaldo fijo del sitio, no en el message del ApiError.
    expect(await screen.findByRole("alert")).toHaveTextContent("No se pudo abrir Diagnóstico.");
    expect(screen.queryByText("pantalla base")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("pantalla base")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it("HU14562 AC50: con la sesión, la campana de avisos aparece en la barra (headerActions) y pide GET /notificaciones", async () => {
    const fetchMock = vi.fn(async (u: RequestInfo | URL) =>
      String(u).startsWith("/api/v1/diagnostico/notificaciones")
        ? Response.json({ no_leidas: 3, items: [], siguiente: null })
        : Response.json(sesion),
    );
    vi.stubGlobal("fetch", fetchMock);
    montar();
    const banner = await screen.findByRole("banner");
    expect(await within(banner).findByRole("button", { name: "Avisos, 3 avisos sin leer" })).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([u]) => String(u) === "/api/v1/diagnostico/notificaciones?limite=20")).toBe(true);
  });

  it("HU14562 AC50: mientras no hay sesión no se pide la campana", () => {
    const fetchMock = vi.fn(() => new Promise<Response>(() => {}));
    vi.stubGlobal("fetch", fetchMock);
    montar();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("button", { name: /^Avisos/ })).toBeNull();
  });
});

describe("HU14577 AC24/AC25 entrada «Saneamientos» con insignia de pendientes", () => {
  const conBackoffice = (gestionarSaneamiento: boolean) => ({ ...sesion, capacidades: { ...sesion.capacidades, backoffice: true, gestionarSaneamiento } });
  const api = (gestionar: boolean, solicitados: number) =>
    vi.fn(async (u: RequestInfo | URL) => {
      const url = String(u);
      if (url.startsWith("/api/v1/diagnostico/notificaciones")) return Response.json({ no_leidas: 0, items: [], siguiente: null });
      if (url === "/api/v1/diagnostico/backoffice/saneamientos/resumen") return Response.json({ solicitados });
      return Response.json(conBackoffice(gestionar));
    });
  const pedidosResumen = (f: ReturnType<typeof api>) => f.mock.calls.filter(([u]) => String(u) === "/api/v1/diagnostico/backoffice/saneamientos/resumen");
  const abrirBackoffice = async () => {
    const [pildora] = await screen.findAllByRole("button", { name: /^Backoffice/ });
    fireEvent.click(pildora);
    return pildora;
  };

  it("con gestionarSaneamiento: la entrada lleva a /backoffice/saneamientos y muestra 12 (en la entrada y en la píldora)", async () => {
    const f = api(true, 12);
    vi.stubGlobal("fetch", f);
    montar();
    await screen.findByText("pantalla base");
    await waitFor(() => expect(pedidosResumen(f)).toHaveLength(1));
    const pildora = await abrirBackoffice();
    await waitFor(() => expect(pildora).toHaveAccessibleName("Backoffice, 12 pendientes"));
    const [entrada] = await screen.findAllByRole("link", { name: "Saneamientos, 12 solicitudes sin tomar" });
    expect(entrada).toHaveAttribute("href", "/backoffice/saneamientos");
    expect(entrada.querySelector(".dock-insignia")).toHaveTextContent(/^12$/);
  });

  it("con 0 no pinta insignia", async () => {
    vi.stubGlobal("fetch", api(true, 0));
    montar();
    await screen.findByText("pantalla base");
    await abrirBackoffice();
    const [entrada] = await screen.findAllByRole("link", { name: "Saneamientos" });
    expect(entrada.querySelector(".dock-insignia")).toBeNull();
  });

  it("con 150 pinta «99+» y el nombre lleva el número real", async () => {
    vi.stubGlobal("fetch", api(true, 150));
    montar();
    await screen.findByText("pantalla base");
    await abrirBackoffice();
    const [entrada] = await screen.findAllByRole("link", { name: "Saneamientos, 150 solicitudes sin tomar" });
    expect(entrada.querySelector(".dock-insignia")).toHaveTextContent(/^99\+$/);
  });

  it("sin gestionarSaneamiento (aunque tenga backoffice) no ve la entrada ni se pide el resumen", async () => {
    const f = api(false, 12);
    vi.stubGlobal("fetch", f);
    montar();
    await screen.findByText("pantalla base");
    await abrirBackoffice();
    expect(screen.queryAllByRole("link", { name: /^Saneamientos/ })).toHaveLength(0);
    expect(screen.getAllByRole("link", { name: "Fuentes obligatorias" }).length).toBeGreaterThan(0);
    expect(pedidosResumen(f)).toHaveLength(0);
  });
});

describe("HU14577 AC25 sondeo del resumen cada 60 s solo con la pestaña visible", () => {
  let visible: DocumentVisibilityState = "visible";
  beforeEach(() => {
    visible = "visible";
    vi.spyOn(document, "visibilityState", "get").mockImplementation(() => visible);
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  const resumen = () => vi.fn(async () => Response.json({ solicitados: 4 }));

  it("pide al montar y cada 60 s mientras está visible; oculta no pide; al volver visible pide en el acto", async () => {
    const f = resumen();
    vi.stubGlobal("fetch", f);
    const { result } = renderHook(() => useSondeoResumenSaneamientos(true));
    await act(async () => {});
    expect(f).toHaveBeenCalledTimes(1);
    expect(result.current.solicitados).toBe(4);
    await act(async () => vi.advanceTimersByTime(60_000));
    expect(f).toHaveBeenCalledTimes(2);
    visible = "hidden";
    await act(async () => vi.advanceTimersByTime(120_000));
    expect(f).toHaveBeenCalledTimes(2);
    visible = "visible";
    await act(async () => document.dispatchEvent(new Event("visibilitychange")));
    expect(f).toHaveBeenCalledTimes(3);
  });

  it("sin la capacidad no pide nunca; si falla conserva el último valor sin error", async () => {
    const f = resumen();
    vi.stubGlobal("fetch", f);
    renderHook(() => useSondeoResumenSaneamientos(false));
    await act(async () => vi.advanceTimersByTime(120_000));
    expect(f).not.toHaveBeenCalled();

    const g = vi.fn().mockResolvedValueOnce(Response.json({ solicitados: 7 })).mockResolvedValue(Response.json({ error: "x" }, { status: 503 }));
    vi.stubGlobal("fetch", g);
    const { result } = renderHook(() => useSondeoResumenSaneamientos(true));
    await act(async () => {});
    expect(result.current.solicitados).toBe(7);
    await act(async () => vi.advanceTimersByTime(60_000));
    expect(g).toHaveBeenCalledTimes(2);
    expect(result.current.solicitados).toBe(7);
  });
});

describe("FE-4 dock: «Diagnósticos» y «Reportes» como enlaces directos (spec-ui §6, V4)", () => {
  const conCapacidades = (extra: Record<string, boolean>) =>
    vi.fn(async (u: RequestInfo | URL) =>
      String(u).startsWith("/api/v1/diagnostico/notificaciones")
        ? Response.json({ no_leidas: 0, items: [], siguiente: null })
        : String(u) === "/api/v1/diagnostico/backoffice/saneamientos/resumen"
          ? Response.json({ solicitados: 0 })
          : Response.json({ ...sesion, capacidades: { ...sesion.capacidades, ...extra } }),
    );
  const enlaces = (nombre: string) => screen.queryAllByRole("link", { name: nombre }).map((a) => a.getAttribute("href"));

  it("con verDiagnosticos ve «Diagnósticos» (/diagnosticos) y sin verReportes no ve «Reportes»", async () => {
    vi.stubGlobal("fetch", conCapacidades({ verDiagnosticos: true }));
    montar();
    await screen.findByText("pantalla base");
    expect(enlaces("Diagnósticos")).toContain("/diagnosticos");
    expect(enlaces("Reportes")).toHaveLength(0);
  });

  it("con verReportes ve «Reportes» (/reportes); con crearDiagnosticos solo, también «Diagnósticos»", async () => {
    vi.stubGlobal("fetch", conCapacidades({ verDiagnosticos: false, crearDiagnosticos: true, verReportes: true }));
    montar();
    await screen.findByText("pantalla base");
    expect(enlaces("Reportes")).toContain("/reportes");
    expect(enlaces("Diagnósticos")).toContain("/diagnosticos");
  });

  it("sin verDiagnosticos ni crearDiagnosticos no hay «Diagnósticos»; la vista global abre «Reportes»", async () => {
    vi.stubGlobal("fetch", conCapacidades({ verDiagnosticos: false, crearDiagnosticos: false, backoffice: true, verEstadisticasGlobales: true }));
    montar();
    await screen.findByText("pantalla base");
    expect(enlaces("Diagnósticos")).toHaveLength(0);
    expect(enlaces("Reportes")).toContain("/reportes");
  });

  it("en el backoffice el orden a la izquierda es Diagnósticos · Reportes · Backoffice", async () => {
    vi.stubGlobal("fetch", conCapacidades({ backoffice: true, verReportes: true }));
    const { container } = montar();
    await screen.findByText("pantalla base");
    const texto = container.textContent ?? "";
    const pos = (s: string) => texto.indexOf(s);
    expect(pos("Diagnósticos")).toBeGreaterThanOrEqual(0);
    expect(pos("Diagnósticos")).toBeLessThan(pos("Reportes"));
    expect(pos("Reportes")).toBeLessThan(pos("Backoffice"));
  });
});

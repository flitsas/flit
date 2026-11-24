import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import GestionPage from "@/app/(app)/reportes/gestion/page";
import DpPage from "@/app/(app)/reportes/dp/page";
import PlataformaPage from "@/app/(app)/reportes/plataforma/page";
import ResumenPage from "@/app/(app)/reportes/page";
import { P } from "@/lib/nav";

// Reportes 2.0 (S3): las pestañas Gestión, Derechos de petición y Plataforma leen y escriben desde, hasta, nit y empresa en la URL.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), apiFetch: (...a: unknown[]) => apiFetch(...a) }));
let sesion: unknown = null;
vi.mock("@/components/ComparendosShell", () => ({ useSesion: () => ({ sesion, cargando: false, error: null }) }));
const replace = vi.fn();
const push = vi.fn();
let busqueda = "";
let ruta = "/reportes/gestion";
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(busqueda), useRouter: () => ({ replace, push }), usePathname: () => ruta }));
const toast = vi.fn();
vi.mock("@/components/Toast", () => ({ useToast: () => ({ show: toast }) }));
vi.mock("recharts", async (original) => ({ ...(await original<typeof import("recharts")>()), ResponsiveContainer: () => null }));

const gestora = (reportes = true) => ({
  usuario: { id: "u1", email: "g@x.test", esSuperAdmin: false },
  permisos: [P.bandejaVer, P.reportesVer],
  empresa: { id: "e1", nombre: "Gestora Uno", capacidades: { derechosPeticion: true, consultaContraventor: false, cambioContraventor: false, ocr: false, reportes } },
});
const super_ = { usuario: { id: "s", email: "s@flit.test", esSuperAdmin: true }, permisos: [], empresa: null };

const operacion = {
  hoy: "2026-11-24",
  conValores: true,
  resumen: { pendientes: 42, valorPendiente: 130_900_000, vehiculos: 30, nuevos30: 7, resueltos30: 3 },
  gestion: { pasos: [{ estadoProceso: "SinProcesar", cantidad: 20 }], tasaExito: 0.25 },
  vehiculos: { conTresOMas: 2, concentracionDiezPorCiento: 0.3, top: [{ placa: "AAA111", cantidad: 5, organismos: 2, valor: 3_020_500 }] },
  infracciones: [{ codigo: "C29", descripcion: "Exceso de velocidad", cantidad: 30, valor: 90_000_000 }],
  evitables: { soat: { cantidad: 4, valor: 5_000_000 }, tecnicoMecanica: { cantidad: 2, valor: 1_000_000 } },
  calendario: [{ periodo: "2026-11", caducan: 1, prescriben: 0 }],
  antiguedad: [{ tramo: "De 1 a 6 meses", cantidad: 5, valor: 1_000_000, desde: "2026-05-24", hasta: "2026-10-24" }],
  dpPorOrganismo: [{ organismoId: "o1", nombre: "Movilidad Bogotá", enviados: 4, favorables: 1, respondidos: 4, diasPromedio: 12.5 }],
};
const gestion = (desde: string | null, hasta: string | null) => ({
  alcance: { empresa: "e1", nit: null, desde, hasta },
  gestion: { conPeriodo: !!desde, operacion, topOrganismos: [{ organismoId: "o1", nombre: "Movilidad Bogotá", cantidad: 40, valor: 120_000_000 }] },
});
const fila = (clave: string, nombre: string, tasaExito: number | null = 0.5) => ({ clave, nombre, enviados: 3, favorables: 1, desfavorables: 1, sinRespuesta: 1, diasPromedio: 4.5, tasaExito });
const dp = (consolidado: unknown[] | null) => ({
  alcance: { empresa: null, nit: null, desde: "2026-11-01", hasta: "2026-11-24" },
  dp: {
    tasaExito: 0.5, ahorro: 604_100, favorables: 1, desfavorables: 1, sinRespuesta: 4, enviados: 3,
    porOrganismo: [fila("o1", "Movilidad Bogotá"), fila("o2", "Tránsito Cali", null)], porInfraccion: [fila("C29", "Exceso de velocidad")],
    cruce: [{ organismoId: "o1", organismo: "Movilidad Bogotá", infraccion: "C29", enviados: 3, favorables: 1, desfavorables: 1 }],
    positivos: [{ comparendoId: "c1", numero: "1100100000001", placa: "AAA111", radicado: "FLIT-1", organismo: "Movilidad Bogotá", fechaRadicacion: "2026-11-02", fechaRespuesta: "2026-11-10", valorAlRadicar: 604_100 }],
    totalPositivos: 1,
    acumulado: [{ dia: "2026-11-01", enviados: 1, respondidos: 0 }, { dia: "2026-11-02", enviados: 3, respondidos: 2 }],
    vencidos: [], totalVencidos: 0, consolidado,
  },
});


/** Pulsa «Descargar Excel» y devuelve el nombre y el XML del libro (STORE: el zip no está comprimido y se lee como texto). */
async function descargarExcel(): Promise<{ nombre: string; xml: string }> {
  let blob: Blob | null = null;
  let nombre = "";
  const crear = vi.spyOn(URL, "createObjectURL").mockImplementation((b) => {
    blob = b as Blob;
    return "blob:prueba";
  });
  const revocar = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  const clic = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    nombre = this.download;
  });
  fireEvent.click(screen.getByTestId("descargar-excel"));
  await waitFor(() => expect(nombre).not.toBe(""));
  const bytes = await new Promise<ArrayBuffer>((ok) => {
    const r = new FileReader();
    r.onload = () => ok(r.result as ArrayBuffer);
    r.readAsArrayBuffer(blob!);
  });
  crear.mockRestore();
  revocar.mockRestore();
  clic.mockRestore();
  return { nombre, xml: new TextDecoder().decode(bytes) };
}

describe("Reportes 2.0 · pestañas de análisis", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    apiFetch.mockImplementation(async (r: string, op?: { query?: Record<string, unknown> }) => {
      if (r === "/reportes/gestion") return gestion((op?.query?.desde as string) ?? null, (op?.query?.hasta as string) ?? null);
      if (r === "/reportes/dp") return dp(sesion === super_ && !op?.query?.empresa ? [{ empresaId: "e1", empresa: "Gestora Uno", organismoId: "o1", organismo: "Movilidad Bogotá", enviados: 3, favorables: 1, desfavorables: 1, sinRespuesta: 1 }] : null);
      if (r === "/admin/reportes/plataforma") return { desde: "2026-11-01", hasta: "2026-11-24", fuentes: [], porDia: [], erroresPorTipo: [], correos: { fallidos: 0, rebotados: 0, motivos: [], recientes: [] }, empresasConMasErrores: [] };
      if (r === "/monitoreo/nits" || r === "/admin/empresas") return [];
      throw new Error(`ruta sin simular: ${r}`);
    });
  });
  afterEach(() => {
    cleanup();
    busqueda = "";
    replace.mockReset();
  });

  it("Gestión sin fechas es todo lo pendiente (como el Inicio), y sin desde ni hasta no se le manda periodo a la API", async () => {
    sesion = gestora();
    ruta = "/reportes/gestion";
    render(<GestionPage />);
    expect(await screen.findByTestId("vehiculos")).toBeTruthy();
    const llamada = apiFetch.mock.calls.find(([r]) => r === "/reportes/gestion")!;
    expect(llamada[1].query).toMatchObject({ desde: null, hasta: null, nit: null, empresa: null });
    expect(screen.getByTestId("gestion-alcance").textContent).toMatch(/Todo lo pendiente a hoy/);
    expect(screen.getByTestId("gestion-corte").textContent).toBe("Pendiente a hoy");
    // Sin periodo, los campos van vacíos: rellenos con el mes harían creer que las cifras son del mes (revisión visual de S0).
    expect((screen.getByLabelText("Desde") as HTMLInputElement).value).toBe("");
    expect((screen.getByLabelText("Hasta") as HTMLInputElement).value).toBe("");
    expect(screen.getByRole("button", { name: "Consultar" })).toBeTruthy();
    expect(screen.getByTestId("gestion-resumen").textContent).toMatch(/Comparendos nuevos en los últimos 30 días/);
    // Sin recorte aquí: Gestión es donde viven las lentes que salieron del Inicio, y sin «Ver en Reportes».
    for (const id of ["vehiculos", "evitables", "antiguedad", "dp-organismos", "top-organismos"]) expect(screen.getByTestId(id)).toBeTruthy();
    expect(screen.queryByTestId("ver-en-reportes")).toBeNull();
    // «Descargar Excel» a la derecha de la fila de filtros, habilitado con los datos cargados.
    expect((screen.getByTestId("descargar-excel") as HTMLButtonElement).disabled).toBe(false);
    expect(screen.getByTestId("descargar-excel").parentElement!.className).toMatch(/ml-auto/);
    expect(within(screen.getByTestId("vehiculos")).getByRole("link", { name: /^AAA111/ }).getAttribute("href")).toBe("/bandeja?placas=AAA111&pagado=false");

    // El Excel lleva las cifras de la pantalla y su corte, con el nombre de la empresa de la gestora.
    const { nombre, xml } = await descargarExcel();
    expect(nombre).toBe("reporte-gestion-gestora-uno-sin-periodo_hoy.xlsx");
    expect(within(screen.getByTestId("gestion-resumen")).getByText("42")).toBeTruthy();
    expect(xml).toMatch(/Pendientes \(activos y sin pagar\)<\/t><\/is><\/c><c r="B2" s="0"><v>42<\/v><\/c><c r="C2" s="0"><v>130900000<\/v>/);
    expect(xml).toContain("Pendiente a hoy");
    expect(xml).toContain("Gestora Uno");
    expect(xml).not.toMatch(/Simit|SinProcesar/);
  });

  it("Gestión con periodo y NIT en la URL: los pide a la API y cada enlace a la bandeja lleva el mismo corte", async () => {
    sesion = gestora();
    ruta = "/reportes/gestion";
    busqueda = "desde=2026-06-01&hasta=2026-11-24&nit=n1";
    render(<GestionPage />);
    expect(await screen.findByTestId("vehiculos")).toBeTruthy();
    expect(apiFetch.mock.calls.find(([r]) => r === "/reportes/gestion")![1].query).toMatchObject({ desde: "2026-06-01", hasta: "2026-11-24", nit: "n1" });
    expect(screen.getByTestId("gestion-corte").textContent).toMatch(/^Comparendos impuestos del .+ al .+$/);
    const corte = "imposicionDesde=2026-06-01&imposicionHasta=2026-11-24&nits=n1";
    expect(within(screen.getByTestId("vehiculos")).getByRole("link", { name: /^AAA111/ }).getAttribute("href")).toBe(`/bandeja?placas=AAA111&pagado=false&${corte}`);
    expect(within(screen.getByTestId("top-organismos")).getByRole("link").getAttribute("href")).toBe(`/bandeja?organismos=o1&pagado=false&${corte}`);
    // El tramo de antigüedad se corta además por el periodo: la intersección de los dos rangos.
    expect(within(screen.getByTestId("antiguedad")).getByRole("link").getAttribute("href")).toBe("/bandeja?pagado=false&imposicionDesde=2026-06-01&imposicionHasta=2026-10-24&nits=n1");
    // DP por organismo se cortó por radicación, no por imposición: su enlace lleva solo el NIT (REX).
    expect(within(screen.getByTestId("dp-organismos")).getByRole("link").getAttribute("href")).toBe("/bandeja?organismos=o1&conDp=true&nits=n1");
    // En Gestión no hay una segunda «tasa» (la canónica es la de DP y del Resumen).
    expect(screen.getByTestId("gestion").textContent).not.toMatch(/%/);
    fireEvent.click(screen.getByRole("button", { name: "Ver todo lo pendiente" }));
    expect(replace).toHaveBeenLastCalledWith("/reportes/gestion?nit=n1", { scroll: false });
  });

  it("Gestión sin periodo: el SuperAdmin elige una empresa y la consulta sin fechas; volver a «Pendiente a hoy» es borrar las dos", async () => {
    sesion = super_;
    ruta = "/reportes/gestion";
    const base = apiFetch.getMockImplementation()!;
    apiFetch.mockImplementation(async (r: string, op?: { query?: Record<string, unknown> }) =>
      r === "/admin/empresas" ? [{ id: "e2", nombre: "Empresa Bravo" }] : base(r, op),
    );
    render(<GestionPage />);
    await screen.findByTestId("vehiculos");
    fireEvent.change(await screen.findByLabelText("Empresa"), { target: { value: "e2" } });
    const consultar = screen.getByRole("button", { name: "Consultar" }) as HTMLButtonElement;
    expect(consultar.disabled).toBe(false);
    fireEvent.click(consultar);
    expect(replace).toHaveBeenLastCalledWith("/reportes/gestion?empresa=e2", { scroll: false });

    // Con la empresa en la URL se consulta con ella y sin fechas; el Excel toma su nombre de la lista ya cargada.
    cleanup();
    apiFetch.mockClear();
    busqueda = "empresa=e2";
    render(<GestionPage />);
    await screen.findByTestId("vehiculos");
    expect(apiFetch.mock.calls.find(([r]) => r === "/reportes/gestion")![1].query).toMatchObject({ empresa: "e2", desde: null, hasta: null });
    await waitFor(() => expect((screen.getByTestId("descargar-excel") as HTMLButtonElement).disabled).toBe(false));
    const llamadasEmpresas = apiFetch.mock.calls.filter(([r]) => r === "/admin/empresas").length;
    const { nombre, xml } = await descargarExcel();
    expect(nombre).toBe("reporte-gestion-empresa-bravo-sin-periodo_hoy.xlsx");
    expect(xml).toContain("Empresa Bravo");
    expect(apiFetch.mock.calls.filter(([r]) => r === "/admin/empresas").length).toBe(llamadasEmpresas);

    // Con periodo, borrar las dos fechas vuelve a «Pendiente a hoy».
    cleanup();
    busqueda = "desde=2026-10-01&hasta=2026-10-31";
    render(<GestionPage />);
    await screen.findByTestId("vehiculos");
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));
    expect(replace).toHaveBeenLastCalledWith("/reportes/gestion", { scroll: false });
  });

  it("Gestión con una sola fecha (Desde o Hasta): «Consultar» apagado y el aviso a la vista", async () => {
    sesion = gestora();
    ruta = "/reportes/gestion";
    for (const campo of ["Desde", "Hasta"]) {
      render(<GestionPage />);
      await screen.findByTestId("vehiculos");
      fireEvent.change(screen.getByLabelText(campo), { target: { value: "2026-10-01" } });
      expect((screen.getByRole("button", { name: "Consultar" }) as HTMLButtonElement).disabled).toBe(true);
      expect(screen.getByText("Elige las dos fechas del periodo.")).toBeTruthy();
      cleanup();
    }
  });

  it("Excel del SuperAdmin: si no se puede leer el nombre de la empresa, error visible y sin descarga (nunca «Empresa elegida»)", async () => {
    sesion = super_;
    ruta = "/reportes/gestion";
    busqueda = "empresa=e2";
    const base = apiFetch.getMockImplementation()!;
    apiFetch.mockImplementation(async (r: string, op?: { query?: Record<string, unknown> }) => {
      if (r === "/admin/empresas") throw new Error("Sin conexión");
      return base(r, op);
    });
    toast.mockClear();
    render(<GestionPage />);
    await screen.findByTestId("vehiculos");
    const boton = screen.getByTestId("descargar-excel") as HTMLButtonElement;
    await waitFor(() => expect(boton.disabled).toBe(false));
    const clic = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    fireEvent.click(boton);
    await waitFor(() => expect(toast).toHaveBeenCalledWith(expect.stringMatching(/^No se pudo leer el nombre de la empresa elegida/), "error"));
    expect(clic).not.toHaveBeenCalled();
    clic.mockRestore();
  });

  it("«Consultar» escribe desde y hasta en la URL con esos nombres, y un rango invertido en la URL no se consulta (R8)", async () => {
    sesion = gestora();
    ruta = "/reportes/gestion";
    render(<GestionPage />);
    await screen.findByTestId("vehiculos");
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-10-01" } });
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2026-10-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));
    expect(replace).toHaveBeenLastCalledWith("/reportes/gestion?desde=2026-10-01&hasta=2026-10-31", { scroll: false });
    cleanup();
    apiFetch.mockClear();
    busqueda = "desde=2026-10-31&hasta=2026-10-01";
    render(<GestionPage />);
    expect((await screen.findAllByText(/La fecha inicial no puede ser posterior a la final/)).length).toBeGreaterThan(0);
    expect(apiFetch.mock.calls.some(([r]) => r === "/reportes/gestion")).toBe(false);
    expect((screen.getByTestId("descargar-excel") as HTMLButtonElement).disabled).toBe(true);
  });

  it("DP: la tasa y el ahorro vienen de la API (los del Resumen) y el consolidado solo lo ve el SuperAdmin con todas", async () => {
    sesion = gestora();
    ruta = "/reportes/dp";
    render(<DpPage />);
    const kpis = await screen.findByTestId("dp-kpis");
    expect(within(kpis).getByText("50 %")).toBeTruthy();
    expect(within(kpis).getByText("$ 604.100")).toBeTruthy();
    expect(screen.getByTestId("dp-por-organismo").textContent).toMatch(/Movilidad Bogotá/);
    // Tasa por fila: la de la API; sin respuestas, «—» y no 0 %.
    const filaCali = within(screen.getByTestId("dp-por-organismo")).getByText("Tránsito Cali").closest("tr")!;
    expect(within(filaCali).getByText("—")).toBeTruthy();
    expect(within(screen.getByTestId("dp-por-organismo")).queryByText("0 %")).toBeNull();
    // El Excel de DP: la tasa de la pantalla (50 %) en la hoja de indicadores, y nada de la fuente.
    const { nombre, xml } = await descargarExcel();
    expect(nombre).toMatch(/^reporte-dp-gestora-uno-\d{4}-\d{2}-01_\d{4}-\d{2}-\d{2}\.xlsx$/);
    expect(xml).toMatch(/Tasa de éxito \(%\)<\/t><\/is><\/c><c r="B2" s="0"><v>50<\/v>/);
    expect(xml).not.toMatch(/Simit|Municipal/);
    expect(screen.queryByTestId("dp-consolidado")).toBeNull();
    // Sin fechas en la URL, el mes en curso (como el resto de Reportes).
    const q = apiFetch.mock.calls.find(([r]) => r === "/reportes/dp")![1].query;
    expect(q.desde).toMatch(/^\d{4}-\d{2}-01$/);
    // En DP el periodo es obligatorio: los campos sí muestran el mes que se está viendo.
    expect((screen.getByLabelText("Desde") as HTMLInputElement).value).toBe(q.desde);
    cleanup();
    sesion = super_;
    render(<DpPage />);
    expect(await screen.findByTestId("dp-consolidado")).toBeTruthy();
    // Patrón de GlobalFilters de Trámites (Samuel): «Empresa» arriba como las fechas, opción vacía «Todas las empresas», sin ayuda debajo.
    const empresa = (await screen.findByLabelText("Empresa")) as HTMLSelectElement;
    expect(empresa.tagName).toBe("SELECT");
    expect(empresa.options[0].textContent).toBe("Todas las empresas");
    expect(empresa.closest("label")!.firstChild!.textContent).toBe("Empresa");
    expect(screen.queryByText(/Sin empresa elegida/)).toBeNull();
  });

  it("Resumen: lee desde, hasta y nit de la URL (vienen de otra pestaña) y «Consultar» los escribe ahí", async () => {
    sesion = gestora();
    ruta = "/reportes";
    busqueda = "desde=2026-06-01&hasta=2026-06-30&nit=n1";
    const base = apiFetch.getMockImplementation()!;
    apiFetch.mockImplementation(async (r: string, op?: { query?: Record<string, unknown> }) => {
      if (r === "/reportes/dashboard" || r === "/reportes/dinero") return new Promise(() => {});
      return base(r, op);
    });
    render(<ResumenPage />);
    await waitFor(() => expect(apiFetch.mock.calls.some(([r]) => r === "/reportes/dashboard")).toBe(true));
    for (const r of ["/reportes/dashboard", "/reportes/dinero"])
      expect(apiFetch.mock.calls.find(([x]) => x === r)![1].query).toMatchObject({ desde: "2026-06-01", hasta: "2026-06-30", nit: "n1" });
    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-07-01" } });
    fireEvent.change(screen.getByLabelText("Hasta"), { target: { value: "2026-07-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));
    expect(replace).toHaveBeenLastCalledWith("/reportes?desde=2026-07-01&hasta=2026-07-31&nit=n1", { scroll: false });
  });

  it("Plataforma: quien no es SuperAdmin no la ve ni la pide; el SuperAdmin sí", async () => {
    sesion = gestora();
    ruta = "/reportes/plataforma";
    render(<PlataformaPage />);
    expect(await screen.findByText("No tienes acceso")).toBeTruthy();
    expect(apiFetch.mock.calls.some(([r]) => r === "/admin/reportes/plataforma")).toBe(false);
    cleanup();
    sesion = super_;
    render(<PlataformaPage />);
    expect(await screen.findByTestId("plataforma-kpis")).toBeTruthy();
    await waitFor(() => expect(apiFetch.mock.calls.some(([r]) => r === "/admin/reportes/plataforma")).toBe(true));
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Inicio from "@/app/(app)/page";
import { P } from "@/lib/nav";

// Rediseño de Comparendos (S2): Inicio con «Para hoy» y la lente de dinero. API y sesión simuladas.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), apiFetch: (...a: unknown[]) => apiFetch(...a) }));
let sesion: unknown = null;
vi.mock("@/components/ComparendosShell", () => ({ useSesion: () => ({ sesion, cargando: false, error: null }) }));
const replace = vi.fn();
let busqueda = "";
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(busqueda), useRouter: () => ({ replace, push: vi.fn() }) }));
// recharts necesita medir el contenedor: en jsdom basta con no pintar el SVG (la lectura accesible va en listas ocultas).
vi.mock("recharts", async (original) => {
  const r = await original<typeof import("recharts")>();
  return { ...r, ResponsiveContainer: () => null };
});

const capacidades = (reportes: boolean) => ({ derechosPeticion: true, consultaContraventor: false, cambioContraventor: false, ocr: false, reportes });
const gestora = (reportes: boolean, permisos = [P.bandejaVer, P.reportesVer]) => ({
  usuario: { id: "u1", email: "g@x.test", esSuperAdmin: false },
  permisos,
  empresa: { id: "e1", nombre: "Gestora Uno", capacidades: capacidades(reportes) },
});

const contadores = { total: 40, proximosAVencer: 3, sinContraventor: 0, rechazadosOt: 1, estadosLegales: { SinNotificar: 0, EnTiempo: 30, Vencido: 10, Caducado: 0, Prescrito: 0 } };
const m = (cantidad: number, valor: number) => ({ cantidad, valor });
const dinero = {
  alcance: { empresa: null, nit: null, desde: "2026-11-01", hasta: "2026-11-19" },
  dinero: {
    hoy: "2026-11-19",
    estados: [
      { estado: "SinNotificar", ...m(0, 0) },
      { estado: "EnTiempo", ...m(30, 126_200_000) },
      { estado: "Vencido", ...m(10, 3_800_000) },
      { estado: "Caducado", ...m(2, 900_000) },
      { estado: "Prescrito", ...m(0, 0) },
    ],
    exigible: m(40, 130_000_000),
    noExigible: m(2, 900_000),
    cobroCoactivo: m(3, 2_500_000),
    descuento: { d50: { ...m(5, 2_000_000), ahorroEstimado: 1_000_000 }, d25: { ...m(0, 0), ahorroEstimado: 0 } },
    organismos: [{ organismoId: "o1", nombre: "Secretaría de Movilidad de Bogotá", cantidad: 42, valor: 130_900_000, exigible: 130_000_000 }],
    pagado: { cantidad: 2, total: 1_208_200, cc: 0, pc: 604_100, bc: 604_100 },
    frentes: {
      proximosAVencer: m(3, 1_812_300),
      sinContraventor: m(0, 0),
      rechazadosOt: m(1, 604_100),
      sinGestion: m(12, 7_249_200),
      vencidos: m(10, 3_800_000),
      caducidadInmediata: m(2, 900_000),
    },
    ahorroLogrado: 604_100,
    mensual: Array.from({ length: 12 }, (_, i) => ({ periodo: `2026-${String(i + 1).padStart(2, "0")}`, valorIngresado: 1000 * i, pagado: 0 })),
  },
};

/** /comparendos/operacion; `conValores` en false = gestora sin el módulo de reportes (pesos en null). */
const operacion = (conValores: boolean) => {
  const v = (x: number) => (conValores ? x : null);
  return {
    empresa: "e1",
    operacion: {
      hoy: "2026-11-19",
      conValores,
      resumen: { pendientes: 42, valorPendiente: v(130_900_000), vehiculos: 30, nuevos30: 7, resueltos30: 3 },
      gestion: {
        pasos: [
          { estadoProceso: "SinProcesar", cantidad: 20 },
          { estadoProceso: "DerechoPeticion", cantidad: 15 },
          { estadoProceso: "RespuestaDesfavorable", cantidad: 3 },
          { estadoProceso: "RespuestaFavorable", cantidad: 1 },
        ],
        tasaExito: 0.25,
      },
      vehiculos: { conTresOMas: 2, concentracionDiezPorCiento: 0.3, top: [{ placa: "AAA111", cantidad: 5, organismos: 2, valor: v(3_020_500) }] },
      infracciones: [{ codigo: "C29", descripcion: "Exceso de velocidad", cantidad: 30, valor: v(90_000_000) }],
      evitables: { soat: { cantidad: 4, valor: v(5_000_000) }, tecnicoMecanica: { cantidad: 2, valor: v(1_000_000) } },
      calendario: ["2026-11", "2026-12", "2027-01", "2027-02", "2027-03", "2027-04"].map((periodo, i) => ({ periodo, caducan: i, prescriben: 0 })),
      antiguedad: [
        { tramo: "Menos de 30 días", cantidad: 7, valor: v(1_000_000), desde: "2026-10-20", hasta: null },
        { tramo: "De 1 a 6 meses", cantidad: 0, valor: v(0), desde: "2026-05-19", hasta: "2026-10-19" },
        { tramo: "Sin fecha de imposición", cantidad: 2, valor: v(500_000), desde: null, hasta: null },
      ],
      dpPorOrganismo: [{ organismoId: "o1", nombre: "Secretaría de Movilidad de Bogotá", enviados: 4, favorables: 1, respondidos: 4, diasPromedio: 12.5 }],
    },
  };
};

const impacto = {
  anio: 2026,
  desde: "2026-01-01",
  hasta: "2026-11-19",
  ahorroLogrado: 26_300_000,
  dp: { enviados: 971, favorables: 45, tasa: 0.046 },
  comparendosGestionados: 1200,
  avisosEnviados: 340,
  cambiosContraventor: { solicitados: 12, aceptados: 9 },
  topEmpresas: [
    { empresaId: "e1", nombre: "Bancolombia", ahorro: 26_000_000, dpFavorables: 44 },
    { empresaId: "e2", nombre: "Renting", ahorro: 300_000, dpFavorables: 1 },
  ],
};

const resumen = {
  hoy: "2026-11-19",
  empresas: { total: 4, activas: 3, pendientes: 1, inactivas: 0 },
  ingesta24h: { consultas: 120, conError: 0, empresasConError: 0 },
  correosFallidos7d: 2,
  correosDesde: "2026-11-12",
  necesitanAlgo: [{ empresaId: "e2", nombre: "Renting", motivos: ["Sin representante legal", "Sin correo para respuestas"] }],
};

describe("Inicio de Comparendos", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    apiFetch.mockImplementation(async (ruta: string, op?: { query?: Record<string, unknown> }) => {
      if (ruta === "/reportes/dinero") return dinero;
      if (ruta === "/comparendos/operacion") return operacion(!!(sesion as { empresa?: { capacidades?: { reportes?: boolean } } })?.empresa?.capacidades?.reportes);
      if (ruta === "/admin/resumen") return op?.query?.partes === "impacto" ? impacto : resumen;
      if (ruta === "/admin/empresas") return [];
      if (ruta === "/comparendos/contadores") return op?.query?.sinGestion ? { ...contadores, total: 12 } : op?.query?.juridico ? { ...contadores, total: 2 } : contadores;
      throw new Error(`ruta sin simular: ${ruta}`);
    });
  });
  afterEach(() => {
    cleanup();
    busqueda = "";
  });

  it("sin el módulo de reportes usa solo los contadores de la bandeja: sin pesos y sin pedir reportes", async () => {
    sesion = gestora(false);
    render(<Inicio />);
    await waitFor(() => expect(screen.getByTestId("frente-proximosAVencer").getAttribute("href")).toBe("/bandeja?rapido=ProximosAVencer"));
    expect(await within(screen.getByTestId("frente-sinGestion")).findByText("12")).toBeTruthy();
    expect(within(screen.getByTestId("frente-caducidadInmediata")).getByText("2")).toBeTruthy();
    expect(apiFetch.mock.calls.some(([ruta]) => String(ruta).startsWith("/reportes"))).toBe(false);
    expect(screen.queryByTestId("kpis-dinero")).toBeNull();
    // R2: la operación también se ve sin el módulo (pide BandejaVer), y sin pesos.
    const vehiculos = await screen.findByTestId("vehiculos");
    expect(within(vehiculos).getByRole("link", { name: /^AAA111: 5 comparendos ante 2 organismos\. Ver en la bandeja$/ }).getAttribute("href")).toBe("/bandeja?placas=AAA111&pagado=false");
    // El tramo sin fecha de imposición no tiene filtro en la bandeja: sin enlace.
    expect(within(screen.getByTestId("antiguedad")).queryByRole("link", { name: /^Sin fecha de imposición/ })).toBeNull();
    expect(within(screen.getByTestId("antiguedad")).getByText("Sin fecha de imposición")).toBeTruthy();
    expect(within(screen.getByTestId("antiguedad")).getByRole("link", { name: /^Menos de 30 días: 7 comparendos\. Ver en la bandeja$/ }).getAttribute("href")).toBe(
      "/bandeja?pagado=false&imposicionDesde=2026-10-20",
    );
    expect(screen.getByRole("button", { name: /^Sin procesar: 20 comparendos\. Ver en la bandeja$/ })).toBeTruthy();
    expect(screen.getByTestId("dp-organismos").textContent).toMatch(/1 de 4 favorables/);
    expect(screen.queryByText(/\$/)).toBeNull();
    // Un frente en cero no lleva a una bandeja vacía, y sin enlace no lleva aria-label (el contenido se lee solo).
    expect(screen.getByTestId("frente-sinContraventor").getAttribute("href")).toBeNull();
    expect(screen.getByTestId("frente-sinContraventor").getAttribute("aria-label")).toBeNull();
  });

  it("con el módulo muestra el dinero abreviado, el periodo de lo pagado y el ahorro posible como estimado", async () => {
    sesion = gestora(true);
    render(<Inicio />);
    const kpis = await screen.findByTestId("kpis-dinero");
    expect(within(kpis).getByText("$ 130 M")).toBeTruthy();
    expect(within(kpis).getAllByText(/1 al 19 de noviembre de 2026/).length).toBeGreaterThan(0);
    expect(within(screen.getByTestId("frente-descuento")).getByText(/estimado/)).toBeTruthy();
    expect(screen.getByTestId("frente-vencidos").getAttribute("href")).toBe("/bandeja?estadoLegal=Vencido");
    expect(apiFetch.mock.calls.filter(([ruta]) => ruta === "/comparendos/contadores")).toHaveLength(0);
    expect(document.body.textContent).not.toMatch(/\bAPI\b/);
    // El title no tapa el nombre accesible: cada enlace dice qué frente es (MAIA).
    expect(screen.getByTestId("frente-vencidos").getAttribute("aria-label")).toMatch(/^Vencidos: 10 comparendos por un valor de \$\s3\.800\.000\. Se pasó el plazo de gestión$/);
    // R2: el dinero es una franja de 4 tarjetas; la tendencia, la dona, los organismos y el coactivo están en Reportes.
    expect(kpis.childElementCount).toBe(4);
    // R4: el dinero va segundo, entre «Para hoy» y la gestión.
    const orden = (a: Element, b: Element) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING;
    expect(orden(screen.getByTestId("para-hoy"), kpis)).toBeTruthy();
    expect(orden(kpis, await screen.findByTestId("gestion"))).toBeTruthy();
    expect(screen.queryByRole("link", { name: /^En cobro coactivo/ })).toBeNull();
    expect(screen.queryByText(/Tendencia de los últimos 12 meses/)).toBeNull();
    // La operación con el módulo trae pesos, y la frase de arriba los dice.
    expect(await screen.findByText(/^42 comparendos pendientes por \$\s130,9 M en 30 vehículos\./)).toBeTruthy();
    expect(within(screen.getByTestId("vehiculos")).getByRole("link", { name: /^AAA111: 5 comparendos ante 2 organismos, por un valor de \$\s3\.020\.500/ })).toBeTruthy();
  });

  it("sin bandeja ni reportes explica por qué no hay nada, sin pedir datos", async () => {
    sesion = gestora(false, []);
    render(<Inicio />);
    expect(await screen.findByText("Tu Inicio está vacío por ahora")).toBeTruthy();
    expect(apiFetch).not.toHaveBeenCalled();
  });

  it("el SuperAdmin con una empresa ve su Inicio y, al volver a «Todas», ya no ve la operación de esa empresa", async () => {
    // Regresión (R2): useCarga conserva los datos de la empresa anterior cuando su clave pasa a null.
    sesion = { usuario: { id: "s", email: "s@flit.test", esSuperAdmin: true }, permisos: [], empresa: null };
    busqueda = "empresa=e1";
    render(<Inicio />);
    expect(await screen.findByTestId("vehiculos")).toBeTruthy();
    expect(apiFetch.mock.calls.some(([ruta, op]) => ruta === "/comparendos/operacion" && op?.query?.empresa === "e1")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Todas las empresas" }));
    expect(await screen.findByTestId("salud")).toBeTruthy();
    expect(screen.queryByTestId("vehiculos")).toBeNull();
    expect(screen.queryByTestId("para-hoy")).toBeNull();
    expect(replace).toHaveBeenLastCalledWith("/", { scroll: false });
  });

  it("el SuperAdmin que pasa de la empresa A a la B no ve la operación ni los pesos de A mientras carga B", async () => {
    // Regresión (REX R3): useCarga conserva los datos de A mientras la clave de B no tiene respuesta.
    sesion = { usuario: { id: "s", email: "s@flit.test", esSuperAdmin: true }, permisos: [], empresa: null };
    busqueda = "empresa=e1";
    const base = apiFetch.getMockImplementation()!;
    apiFetch.mockImplementation(async (ruta: string, op?: { query?: Record<string, unknown> }) => {
      if (ruta === "/admin/empresas") return [{ id: "e1", nombre: "Empresa A" }, { id: "e2", nombre: "Empresa B" }];
      // B nunca responde en este test: se queda cargando.
      if (op?.query?.empresa === "e2") return new Promise(() => {});
      return base(ruta, op);
    });
    render(<Inicio />);
    expect(await screen.findByTestId("vehiculos")).toBeTruthy();
    expect(await screen.findByTestId("kpis-dinero")).toBeTruthy();
    expect(screen.getByText(/^42 comparendos pendientes/)).toBeTruthy();
    fireEvent.change(await screen.findByLabelText("Empresa"), { target: { value: "e2" } });
    await waitFor(() => expect(screen.queryByTestId("vehiculos")).toBeNull());
    expect(screen.queryByTestId("kpis-dinero")).toBeNull();
    expect(screen.queryByText(/^42 comparendos pendientes/)).toBeNull();
    // Los frentes de B todavía no tienen cifra: se ven en «—», no con las de A.
    expect(within(screen.getByTestId("frente-vencidos")).getByText("—")).toBeTruthy();
    expect(apiFetch.mock.calls.some(([ruta, op]) => ruta === "/reportes/dinero" && op?.query?.partes === "inicio")).toBe(true);
  });

  it("el SuperAdmin con «Todas» ve la salud de la plataforma y las empresas que necesitan algo", async () => {
    sesion = { usuario: { id: "s", email: "s@flit.test", esSuperAdmin: true }, permisos: [], empresa: null };
    render(<Inicio />);
    const salud = await screen.findByTestId("salud");
    expect(within(salud).getByRole("link", { name: /^Correos fallidos o rebotados en 7 días: 2\./ }).getAttribute("href")).toBe("/mensajes?estado=Fallido,Rebotado&desde=2026-11-12");
    expect(within(salud).getByText("3 de 4")).toBeTruthy();
    const necesitan = screen.getByTestId("necesitan-algo");
    expect(within(necesitan).getByRole("link", { name: /^Renting: Sin representante legal, Sin correo para respuestas\. Ir al Parametrizador$/ }).getAttribute("href")).toBe("/admin/empresas");
    // R4: sin OCR por revisar ni DP del mes en la salud (3 tarjetas), y el impacto del año entre la salud y las empresas.
    expect(within(salud).queryByText("OCR por revisar")).toBeNull();
    expect(within(salud).queryByText("Derechos de petición del mes")).toBeNull();
    const impactoPanel = await screen.findByTestId("impacto");
    expect(within(impactoPanel).getByText("Impacto de FLIT en 2026")).toBeTruthy();
    expect(within(impactoPanel).getByText("$ 26,3 M")).toBeTruthy();
    expect(within(impactoPanel).getByText(/^45 favorables \(4,6 % de los respondidos\)$/)).toBeTruthy();
    expect(within(impactoPanel).getByText("Aceptados de 12 solicitados")).toBeTruthy();
    expect(within(impactoPanel).getByText("Bancolombia")).toBeTruthy();
    const despues = (a: Element, b: Element) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING;
    expect(despues(salud, impactoPanel)).toBeTruthy();
    expect(despues(impactoPanel, necesitan)).toBeTruthy();
    // Con «Todas» no se pide la operación de ninguna empresa (eso es al elegir una); el impacto va en su propia llamada.
    expect(apiFetch.mock.calls.map(([ruta, op]) => `${ruta}${op?.query?.partes ? `?partes=${op.query.partes}` : ""}`).sort()).toEqual([
      "/admin/empresas",
      "/admin/resumen",
      "/admin/resumen?partes=impacto",
    ]);
  });
});

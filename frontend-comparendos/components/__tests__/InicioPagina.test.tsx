import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import Inicio from "@/app/(app)/page";
import { P } from "@/lib/nav";

// Rediseño de Comparendos (S2): Inicio con «Para hoy» y la lente de dinero. API y sesión simuladas.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), apiFetch: (...a: unknown[]) => apiFetch(...a) }));
let sesion: unknown = null;
vi.mock("@/components/ComparendosShell", () => ({ useSesion: () => ({ sesion, cargando: false, error: null }) }));
const replace = vi.fn();
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(), useRouter: () => ({ replace, push: vi.fn() }) }));
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

describe("Inicio de Comparendos", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    apiFetch.mockImplementation(async (ruta: string, op?: { query?: Record<string, unknown> }) => {
      if (ruta === "/reportes/dinero") return dinero;
      if (ruta === "/comparendos/contadores") return op?.query?.sinGestion ? { ...contadores, total: 12 } : op?.query?.juridico ? { ...contadores, total: 2 } : contadores;
      throw new Error(`ruta sin simular: ${ruta}`);
    });
  });
  afterEach(cleanup);

  it("sin el módulo de reportes usa solo los contadores de la bandeja: sin pesos y sin pedir reportes", async () => {
    sesion = gestora(false);
    render(<Inicio />);
    await waitFor(() => expect(screen.getByTestId("frente-proximosAVencer").getAttribute("href")).toBe("/bandeja?rapido=ProximosAVencer"));
    expect(await within(screen.getByTestId("frente-sinGestion")).findByText("12")).toBeTruthy();
    expect(within(screen.getByTestId("frente-caducidadInmediata")).getByText("2")).toBeTruthy();
    expect(apiFetch.mock.calls.some(([ruta]) => String(ruta).startsWith("/reportes"))).toBe(false);
    expect(screen.queryByTestId("kpis-dinero")).toBeNull();
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
    const coactivo = screen.getByRole("link", { name: /^En cobro coactivo: 3 comparendos por un valor de \$\s2\.500\.000/ });
    expect(coactivo.getAttribute("href")).toBe("/bandeja?cobroCoactivo=true");
    // La lectura accesible del gráfico por organismo separa lo cobrable de lo caducado (valor − exigible).
    expect(screen.getByText(/^Secretaría de Movilidad de Bogotá: Se puede cobrar \$\s130\.000\.000, Caducado o prescrito \$\s900\.000$/)).toBeTruthy();
  });

  it("sin bandeja ni reportes explica por qué no hay nada, sin pedir datos", async () => {
    sesion = gestora(false, []);
    render(<Inicio />);
    expect(await screen.findByText("Tu Inicio está vacío por ahora")).toBeTruthy();
    expect(apiFetch).not.toHaveBeenCalled();
  });

  it("el SuperAdmin elige una empresa antes de ver nada", async () => {
    sesion = { usuario: { id: "s", email: "s@flit.test", esSuperAdmin: true }, permisos: [], empresa: null };
    render(<Inicio />);
    expect(await screen.findByText("Elige una empresa")).toBeTruthy();
    expect(apiFetch.mock.calls.map(([ruta]) => ruta)).toEqual(["/admin/empresas"]); // solo el selector de empresas
  });
});

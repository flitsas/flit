// Rediseño del Inicio en 3 dashboards (2026-11-17) — franjas 2 y 3 del organismo: tiempos de gestión, cola crítica,
// resoluciones con causales y trámites por cliente corporativo.
//
// Lo que se fija aquí: el rango de fechas mueve SOLO estas franjas (la cola viva no), los tiempos van en DÍAS y sin
// meta, el porcentaje de causales se rotula como lo que es (puede sumar más de 100) y cada panel cae por su cuenta.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  OtClientsByFamily,
  OtDrilldown,
  OtOperationalPanel,
  OtRejectionReasons,
  OtReportSummary,
} from "@/lib/api/ot-metrics";
import { OtDashboard } from "../OtDashboard";

const fetchOtProfile = vi.fn();
const fetchOtOperationalPanel = vi.fn();
const fetchOtDrilldown = vi.fn();
const fetchOtReport = vi.fn();
const fetchOtRejectionReasons = vi.fn();
const fetchOtClientsByFamily = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: (...args: unknown[]) => fetchOtProfile(...args),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn().mockResolvedValue([]),
}));

vi.mock("@/lib/api/ot-metrics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/ot-metrics")>()),
  fetchOtOperationalPanel: (...args: unknown[]) => fetchOtOperationalPanel(...args),
  fetchOtDrilldown: (...args: unknown[]) => fetchOtDrilldown(...args),
  fetchOtReport: (...args: unknown[]) => fetchOtReport(...args),
  fetchOtRejectionReasons: (...args: unknown[]) => fetchOtRejectionReasons(...args),
  fetchOtClientsByFamily: (...args: unknown[]) => fetchOtClientsByFamily(...args),
}));

vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: vi.fn().mockResolvedValue([]),
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";

const PANEL: OtOperationalPanel = {
  movimiento: { entregadosHoy: 2, decididosHoy: 0, pendientesTotal: 5, tiempoMedianoDecisionHoras: 6 },
  cola: { porRevisar: 3, esperandoAsignarPlaca: 2, enEsperaDelCliente: 0, prioritarios: 1 },
  antiguedad: { hasta1Dia: 2, entre2y3Dias: 0, entre4y7Dias: 3, masDe7Dias: 0, prioritariosEstancados: 0 },
};

function resumen(overrides: Partial<OtReportSummary> = {}): OtReportSummary {
  return {
    total: 10,
    enRevision: 2,
    esperandoPlaca: 0,
    esperandoCliente: 0,
    aprobados: 6,
    enSubsanacion: 1,
    rechazados: 1,
    anulados: 0,
    revocados: 0,
    otros: 0,
    decididos: 8,
    devoluciones: 2,
    devolucionesPromedio: 0.2,
    tiempoMedianoHoras: 30,
    tiempoPromedioHoras: 40,
    tiempoP90Horas: 80,
    tiempoMedianoAprobacionHoras: 30,
    distribucionTiempos: [],
    granularidad: "dia",
    serie: [
      { bucket: "2026-11-15", label: "15 nov", desde: "2026-11-15", hasta: "2026-11-15", radicados: 3, aprobados: 2, rechazados: 0, tiempoPromedioDecisionDias: 1.5 },
      { bucket: "2026-11-16", label: "16 nov", desde: "2026-11-16", hasta: "2026-11-16", radicados: 0, aprobados: 0, rechazados: 0, tiempoPromedioDecisionDias: null },
      { bucket: "2026-11-17", label: "17 nov", desde: "2026-11-17", hasta: "2026-11-17", radicados: 2, aprobados: 1, rechazados: 1, tiempoPromedioDecisionDias: 3 },
    ],
    ...overrides,
  };
}

function cola(n: number): OtDrilldown {
  return {
    bucket: "por_revisar",
    total: n,
    omitidos: 0,
    items: Array.from({ length: n }, (_, i) => ({
      procedureInstanceId: `p-${i}`,
      referenceNumber: `RAD-${String(i).padStart(4, "0")}`,
      placa: i === 0 ? "ABC123" : null,
      vin: i === 0 ? null : `VIN${i}`,
      clientTenantId: "t-1",
      clientTenantName: "Flota Andina S.A.S.",
      status: "entregado",
      familia: "MATRICULAS",
      tipoTramite: "Matrícula inicial",
      prioritario: false,
      diasEsperando: i + 0.5,
    })),
  };
}

const CAUSALES: OtRejectionReasons = {
  causales: [
    { reasonId: "r1", code: "C1", description: "Documento ilegible", rechazos: 3, pct: 60 },
    { reasonId: "r2", code: "C2", description: "Firma faltante", rechazos: 2, pct: 40 },
    { reasonId: "r3", code: "C3", description: "SOAT vencido", rechazos: 2, pct: 40 },
    { reasonId: "r4", code: "C4", description: "Impuesto pendiente", rechazos: 1, pct: 20 },
    { reasonId: "r5", code: "C5", description: "Sin uso", rechazos: 0, pct: 0 },
  ],
  totalRechazos: 5,
  rechazosSinCausal: 0,
  promedioCausalesPorRechazo: 1.6,
};

const CLIENTES: OtClientsByFamily = {
  total: 7,
  clientes: [
    { clientTenantId: "t-1", nombre: "Flota Andina S.A.S.", matriculas: 3, traspasos: 1, otros: 1, total: 5 },
    { clientTenantId: "t-2", nombre: "Transportes Zeta S.A.S.", matriculas: 0, traspasos: 2, otros: 0, total: 2 },
  ],
};

describe("OtDashboard — franjas 2 y 3", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    fetchOtProfile.mockResolvedValue({ transitOfficeId: OT_ID });
    fetchOtOperationalPanel.mockResolvedValue(PANEL);
    fetchOtDrilldown.mockResolvedValue(cola(7));
    fetchOtReport.mockResolvedValue({ resumen: resumen(), total: 10, page: 1, pageSize: 1, filas: [] });
    fetchOtRejectionReasons.mockResolvedValue(CAUSALES);
    fetchOtClientsByFamily.mockResolvedValue(CLIENTES);
  });

  it("los tiempos de gestión van en días, sin meta, y omiten los periodos sin decisiones", async () => {
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-tiempos");

    expect(within(panel).getByText("Tiempos de gestión")).toBeInTheDocument();
    expect(await within(panel).findByText("Tiempo promedio de decisión, en días, por día")).toBeInTheDocument();
    // Resumen accesible de la gráfica: solo los periodos con decisiones, formateados en días.
    expect(await within(panel).findByText("15 nov: 1,5 d")).toBeInTheDocument();
    expect(within(panel).getByText("17 nov: 3 d")).toBeInTheDocument();
    expect(within(panel).queryByText(/16 nov/)).not.toBeInTheDocument();
    expect(within(panel).queryByText(/meta|SLA/i)).not.toBeInTheDocument();
  });

  it("sin decisiones en el periodo lo dice con palabras", async () => {
    fetchOtReport.mockResolvedValue({
      resumen: resumen({ serie: resumen().serie.map((p) => ({ ...p, tiempoPromedioDecisionDias: null })) }),
      total: 0,
      page: 1,
      pageSize: 1,
      filas: [],
    });
    render(<OtDashboard />);

    expect(
      await within(await screen.findByTestId("ot-inicio-tiempos")).findByText("No hubo decisiones en el periodo elegido."),
    ).toBeInTheDocument();
  });

  it("la cola crítica muestra los 5 que más esperan, con su tiempo en espera y el enlace para revisarlos", async () => {
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-cola-critica");

    // Sin SLA en el dominio: el título no habla de vencimiento.
    expect(within(panel).getByRole("heading", { name: "Cola crítica" })).toBeInTheDocument();
    expect(within(panel).queryByText(/vencimiento/i)).not.toBeInTheDocument();
    await within(panel).findByText("RAD-0006");
    const filas = within(panel).getAllByRole("row").slice(1);
    expect(filas).toHaveLength(5);
    // De mayor a menor espera: el primero es el de 6,5 días.
    expect(within(filas[0]).getByText("RAD-0006")).toBeInTheDocument();
    expect(within(filas[0]).getByText("6 días")).toBeInTheDocument();
    expect(within(panel).queryByText("RAD-0000")).not.toBeInTheDocument();

    expect(within(panel).getByRole("link", { name: "Revisar el trámite RAD-0006" })).toHaveAttribute(
      "href",
      `/admin/transit-offices/${OT_ID}/client-procedures?vin=VIN6&status=entregado`,
    );
    expect(within(panel).getByRole("link", { name: "Ver todos" })).toHaveAttribute(
      "href",
      `/admin/transit-offices/${OT_ID}/client-procedures?status=entregado`,
    );
    expect(fetchOtDrilldown).toHaveBeenCalledWith(expect.objectContaining({ transitOfficeId: OT_ID }), "por_revisar", expect.anything());
  });

  it("resoluciones: dona aprobados contra rechazados (con subsanación) y top 3 de causales rotulado", async () => {
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-resoluciones");

    // 6 aprobados contra 1 rechazado + 1 en subsanación → 75 %.
    expect(await within(panel).findByTestId("ot-inicio-pct-aprobados")).toHaveTextContent("75 % de lo decidido se aprobó");
    const causales = within(panel).getByTestId("ot-inicio-causales");
    expect(within(causales).getByText(/Un rechazo puede tener varias/)).toBeInTheDocument();
    expect(await within(causales).findByText("Documento ilegible")).toBeInTheDocument();
    expect(within(causales).getByText("Firma faltante")).toBeInTheDocument();
    expect(within(causales).getByText("SOAT vencido")).toBeInTheDocument();
    expect(within(causales).queryByText("Impuesto pendiente")).not.toBeInTheDocument();
    expect(within(causales).getAllByRole("progressbar")).toHaveLength(3);
  });

  it("la dona lleva el % de aprobados al centro, como el prototipo", async () => {
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-resoluciones");
    await within(panel).findByTestId("ot-inicio-pct-aprobados");

    const svg = panel.querySelector('svg[viewBox="0 0 120 120"]') as SVGElement;
    expect(svg).toHaveTextContent("75 %");
    expect(svg).toHaveTextContent("Aprobados");
  });

  it("la barra de una causal nunca pasa de 100 aunque el dato venga mal", async () => {
    fetchOtRejectionReasons.mockResolvedValue({
      ...CAUSALES,
      causales: [{ reasonId: "r9", code: "C9", description: "Dato inflado", rechazos: 6, pct: 120 }],
    });
    render(<OtDashboard />);
    const causales = await within(await screen.findByTestId("ot-inicio-resoluciones")).findByTestId("ot-inicio-causales");

    const barra = await within(causales).findByRole("progressbar", { name: "Dato inflado" });
    expect(barra).toHaveAttribute("aria-valuenow", "100");
    expect(barra).toHaveAttribute("aria-valuemax", "100");
    expect(within(causales).getByText("100 %")).toBeInTheDocument();
  });

  it("las pestañas filtran por familia sin pedir las demás", async () => {
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-resoluciones");
    await within(panel).findByTestId("ot-inicio-pct-aprobados");

    expect(fetchOtRejectionReasons).toHaveBeenCalledTimes(1);
    expect(fetchOtRejectionReasons).toHaveBeenLastCalledWith(expect.objectContaining({ family: undefined }), expect.anything());

    await userEvent.click(within(panel).getByRole("tab", { name: "Traspasos" }));
    await waitFor(() =>
      expect(fetchOtRejectionReasons).toHaveBeenLastCalledWith(expect.objectContaining({ family: "TRASPASO" }), expect.anything()),
    );
    expect(fetchOtReport).toHaveBeenCalledWith(expect.objectContaining({ family: "TRASPASO", pageSize: 1 }), expect.anything());
    expect(fetchOtRejectionReasons).toHaveBeenCalledTimes(2);
  });

  it("trámites por cliente corporativo: solo los 5 que más radicaron, de mayor a menor, y el resto en palabras", async () => {
    const siete = Array.from({ length: 7 }, (_, i) => ({
      clientTenantId: `t-${i}`,
      nombre: `Cliente ${i + 1}`,
      matriculas: i + 1,
      traspasos: 0,
      otros: 0,
      total: i + 1,
    }));
    fetchOtClientsByFamily.mockResolvedValue({ total: 28, clientes: siete });
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-clientes");
    expect(await within(panel).findByText("Cliente 7")).toBeInTheDocument();
    expect(within(panel).getByText("Cliente 3")).toBeInTheDocument();
    expect(within(panel).queryByText("Cliente 2")).not.toBeInTheDocument();
    expect(within(panel).getByTestId("ot-inicio-clientes-otros")).toHaveTextContent("2 clientes más radicaron trámites en el periodo");
  });

  it("trámites por cliente corporativo: una fila por empresa con su reparto por familia", async () => {
    render(<OtDashboard />);
    const panel = await screen.findByTestId("ot-inicio-clientes");

    const andina = (await within(panel).findByText("Flota Andina S.A.S.")).closest("tr") as HTMLElement;
    const celdas = within(andina).getAllByRole("cell").map((c) => c.textContent);
    expect(celdas).toEqual(["Flota Andina S.A.S.", "3", "1", "1", "5"]);
    expect(within(panel).getByRole("columnheader", { name: "Matrícula inicial" })).toBeInTheDocument();
    expect(within(panel).getByRole("columnheader", { name: "Otros trámites" })).toBeInTheDocument();
  });

  it("el rango de fechas mueve las franjas 2 y 3, no la cola viva", async () => {
    render(<OtDashboard />);
    await screen.findByTestId("ot-inicio-clientes");
    await waitFor(() => expect(fetchOtClientsByFamily).toHaveBeenCalledTimes(1));
    const primeraLlamada = fetchOtClientsByFamily.mock.calls[0][0] as { from: string; to: string };
    expect(primeraLlamada.from).toBeTruthy();
    expect(primeraLlamada.to).toBeTruthy();

    await userEvent.selectOptions(screen.getByLabelText("Periodo"), "ultimos_7");

    await waitFor(() => expect(fetchOtClientsByFamily).toHaveBeenCalledTimes(2));
    const segunda = fetchOtClientsByFamily.mock.calls[1][0] as { from: string };
    expect(segunda.from > primeraLlamada.from).toBe(true);
    // La cola viva (panel y cola crítica) no se vuelve a pedir.
    expect(fetchOtOperationalPanel).toHaveBeenCalledTimes(1);
    expect(fetchOtDrilldown).toHaveBeenCalledTimes(1);
  });

  it("un fallo de un panel no tumba a los demás ni a las cifras de la cola", async () => {
    fetchOtClientsByFamily.mockRejectedValue(new Error("caído"));
    render(<OtDashboard />);

    expect(
      await within(await screen.findByTestId("ot-inicio-clientes")).findByText("No se pudo cargar el volumen por cliente."),
    ).toBeInTheDocument();
    expect(await screen.findByText("Radicados ingresados hoy")).toBeInTheDocument();
    expect(await within(screen.getByTestId("ot-inicio-tiempos")).findByText("15 nov: 1,5 d")).toBeInTheDocument();
  });
});

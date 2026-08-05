// Reportes del organismo de tránsito. Antes de esto la pantalla era un cartel de "en
// construcción": el organismo trabajaba dentro de FLIT sin ningún instrumento propio.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type {
  OtClientCompanyOption,
  OtDrilldown,
  OtOperationalPanel,
  OtPerformance,
  OtRejectionReasons,
} from "@/lib/api/ot-metrics";

const mocks = vi.hoisted(() => ({
  fetchOtOperationalPanel: vi.fn(),
  fetchOtPerformance: vi.fn(),
  fetchOtRejectionReasons: vi.fn(),
  fetchOtClientCompanies: vi.fn(),
  fetchOtDrilldown: vi.fn(),
}));

vi.mock("@/lib/api/ot-metrics", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ot-metrics")>();
  return {
    ...actual,
    fetchOtOperationalPanel: mocks.fetchOtOperationalPanel,
    fetchOtPerformance: mocks.fetchOtPerformance,
    fetchOtRejectionReasons: mocks.fetchOtRejectionReasons,
    fetchOtClientCompanies: mocks.fetchOtClientCompanies,
    fetchOtDrilldown: mocks.fetchOtDrilldown,
  };
});

import { OtReportsConsole } from "@/components/admin/transit-offices/OtReportsConsole";

// 142 pendientes de los que solo 93 son accionables por el organismo: el caso que obliga a
// explicar por qué el desglose no suma el total.
const PANEL: OtOperationalPanel = {
  movimiento: {
    entregadosHoy: 37,
    decididosHoy: 29,
    pendientesTotal: 142,
    tiempoMedianoDecisionHoras: 6,
  },
  cola: { porRevisar: 58, esperandoAsignarPlaca: 35, enEsperaDelCliente: 49 },
  antiguedad: {
    hasta1Dia: 44,
    entre2y3Dias: 51,
    entre4y7Dias: 30,
    masDe7Dias: 17,
    prioritariosEstancados: 6,
  },
};

const PERFORMANCE: OtPerformance = {
  revisores: [
    {
      userId: "u1",
      displayName: "Carla Revisora",
      decididos: 128,
      aprobados: 114,
      aprobacionPct: 89,
      rechazados: 14,
      rechazoPct: 11,
      tiempoMedianoHoras: 6,
      vuelvenARechazarsePct: 4,
    },
  ],
  empresas: [
    {
      tenantId: "t1",
      name: "Flota Andina S.A.S.",
      entregados: 210,
      aprobados: 180,
      pasanPrimeraPct: 88,
      devolucionesPromedio: 0.1,
    },
    // Empresa sin nada decidido en el periodo: no puede mostrarse como «100 % a la primera».
    {
      tenantId: "t2",
      name: "Renting Sin Actividad S.A.",
      entregados: 0,
      aprobados: 0,
      pasanPrimeraPct: 0,
      devolucionesPromedio: 0,
    },
  ],
};

// Los porcentajes suman más de 100 a propósito: un rechazo puede llevar varias causales.
const REASONS: OtRejectionReasons = {
  causales: [
    {
      reasonId: "r1",
      code: "improntas_mal_cargadas",
      description: "Improntas mal cargadas",
      rechazos: 98,
      pct: 46,
    },
    {
      reasonId: "r2",
      code: "improntas_borrosas",
      description: "Improntas están borrosas",
      rechazos: 68,
      pct: 32,
    },
    // Una causal del catálogo que nadie usó: se filtra de las barras pero no rompe nada.
    { reasonId: "r3", code: "otros_matricula", description: "Otros", rechazos: 0, pct: 0 },
  ],
  totalRechazos: 214,
  rechazosSinCausal: 12,
  promedioCausalesPorRechazo: 1.8,
};

// Nombres deliberadamente distintos a los de PERFORMANCE.empresas: este filtro solo puebla el
// <select>, y si compartiera nombres con la tabla de calidad los tests con getByText colisionarían.
const COMPANIES: OtClientCompanyOption[] = [
  { tenantId: "c1", name: "Distribuidora del Valle S.A.S." },
  { tenantId: "c2", name: "Comercializadora Andina Ltda." },
];

const DRILLDOWN: OtDrilldown = {
  bucket: "decididos_hoy",
  total: 2,
  omitidos: 0,
  items: [
    {
      procedureInstanceId: "p1",
      referenceNumber: "REF-001",
      placa: "ABC123",
      vin: null,
      clientTenantId: "t1",
      clientTenantName: "Flota Andina S.A.S.",
      status: "aprobado",
      modalidadEntrada: "matricula_inicial",
      prioritario: false,
      diasEsperando: 1.5,
    },
    {
      procedureInstanceId: "p2",
      referenceNumber: "REF-002",
      placa: null,
      vin: "9BWZZZ377VT004251",
      clientTenantId: "t1",
      clientTenantName: "Flota Andina S.A.S.",
      status: "rechazado",
      modalidadEntrada: "matricula_inicial",
      prioritario: true,
      diasEsperando: null,
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchOtOperationalPanel.mockResolvedValue(PANEL);
  mocks.fetchOtPerformance.mockResolvedValue(PERFORMANCE);
  mocks.fetchOtRejectionReasons.mockResolvedValue(REASONS);
  mocks.fetchOtClientCompanies.mockResolvedValue(COMPANIES);
  mocks.fetchOtDrilldown.mockResolvedValue(DRILLDOWN);
});

describe("Reportes del organismo de tránsito", () => {
  it("muestra el movimiento del día y la antigüedad de la cola", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByTestId("ot-reports-operational")).toBeInTheDocument());
    expect(screen.getByText("Entregados hoy")).toBeInTheDocument();
    expect(screen.getByText("142")).toBeInTheDocument();
    expect(screen.getByText("+7 días")).toBeInTheDocument();
  });

  it("explica por qué el desglose de la cola no suma el total", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    // Sin este aviso, "58 + 35" contra "142 pendientes" se lee como un error del reporte.
    await waitFor(() =>
      expect(screen.getByText(/El desglose suma 93 y hay 142 pendientes/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/esperan algo del cliente/)).toBeInTheDocument();
  });

  it("avisa de los prioritarios estancados", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() =>
      expect(screen.getByTestId("ot-reports-prioritarios")).toHaveTextContent(
        /6 trámites marcados como prioritario llevan más de 3 días sin tocar/,
      ),
    );
  });

  it("no resalta en ámbar el tramo +7 días cuando está en cero", async () => {
    mocks.fetchOtOperationalPanel.mockResolvedValue({
      ...PANEL,
      antiguedad: { ...PANEL.antiguedad, masDe7Dias: 0 },
    });
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    // El ámbar señala trabajo atrasado. Encendido sobre un cero enseña a ignorarlo cuando importa.
    const cero = await screen.findByText("+7 días");
    expect(cero.className).not.toMatch(/amber/);
  });

  it("rotula los motivos como % de rechazos que incluyen la causal, no como reparto", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByTestId("ot-reports-reasons")).toBeInTheDocument());
    expect(screen.getByText("Improntas mal cargadas")).toBeInTheDocument();
    // La aclaración es parte del dato: leído como reparto, el porcentaje engaña.
    expect(
      screen.getByText(/la suma puede pasar del 100 %/),
    ).toBeInTheDocument();
  });

  it("expone el promedio de causales por rechazo como indicador de salud", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    // Si este número se acerca al tamaño del catálogo, alguien está marcando todo.
    await waitFor(() =>
      expect(screen.getByText("Causales por rechazo (promedio)")).toBeInTheDocument(),
    );
    expect(screen.getByText("1.8")).toBeInTheDocument();
  });

  it("muestra volumen y calidad juntos en el equipo de revisores", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByTestId("ot-reports-reviewers")).toBeInTheDocument());
    // El conteo solo premia a quien decide rápido y mal: por eso va con % aprobado y % rechazo.
    expect(screen.getByText("% aprobado")).toBeInTheDocument();
    expect(screen.getByText("Carla Revisora")).toBeInTheDocument();
    expect(screen.getByText("89 %")).toBeInTheDocument();
  });

  it("muestra la calidad por empresa cliente", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByTestId("ot-reports-companies")).toBeInTheDocument());
    expect(screen.getByText("Flota Andina S.A.S.")).toBeInTheDocument();
    expect(screen.getByText("Pasan a la primera")).toBeInTheDocument();
    expect(screen.getByText("88 %")).toBeInTheDocument();
  });

  it("no muestra un porcentaje sin base para empresas sin nada decidido", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    // «100 %» sobre cero aprobados se leería como una empresa impecable; el dato correcto es «—».
    const fila = (await screen.findByText("Renting Sin Actividad S.A.")).closest("tr");
    expect(fila).not.toBeNull();
    expect(fila!.textContent).toContain("—");
  });

  it("muestra el error sin romper la pantalla si la carga falla", async () => {
    mocks.fetchOtOperationalPanel.mockRejectedValue(new Error("Backend caído"));
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByText("Backend caído")).toBeInTheDocument());
  });

  it("carga el filtro de empresa y lo envía en la consulta al recargar", async () => {
    const user = userEvent.setup();
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(mocks.fetchOtClientCompanies).toHaveBeenCalledWith("ot-1"));
    const select = await screen.findByLabelText("Filtrar por empresa");
    expect(screen.getByText("Distribuidora del Valle S.A.S.")).toBeInTheDocument();

    await user.selectOptions(select, "c1");
    await user.click(screen.getByRole("button", { name: /Actualizar/ }));

    await waitFor(() =>
      expect(mocks.fetchOtOperationalPanel).toHaveBeenLastCalledWith(
        expect.objectContaining({ clientTenantId: "c1" }),
      ),
    );
  });

  it("abre el drill-down de un bloque y muestra la lista de trámites con link para gestionar", async () => {
    const user = userEvent.setup();
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByText("Decididos hoy")).toBeInTheDocument());
    await user.click(screen.getByText("Decididos hoy"));

    expect(mocks.fetchOtDrilldown).toHaveBeenCalledWith(
      expect.objectContaining({ from: expect.any(String), to: expect.any(String) }),
      "decididos_hoy",
    );

    await waitFor(() => expect(screen.getByTestId("ot-reports-drilldown")).toBeInTheDocument());
    expect(screen.getByText("REF-001")).toBeInTheDocument();
    expect(screen.getByText("REF-002")).toBeInTheDocument();
    // El trámite prioritario debe destacarse: es el peor indicador de la cola.
    expect(screen.getByText("Prioritario")).toBeInTheDocument();

    const links = screen.getAllByRole("link", { name: "Ir a gestionar" });
    expect(links[0]).toHaveAttribute(
      "href",
      "/admin/transit-offices/ot-1/client-procedures?placa=ABC123&status=aprobado",
    );
    expect(links[1]).toHaveAttribute(
      "href",
      "/admin/transit-offices/ot-1/client-procedures?vin=9BWZZZ377VT004251&status=rechazado",
    );
  });

  it("avisa cuántos trámites quedaron fuera del tope en el drill-down", async () => {
    const user = userEvent.setup();
    mocks.fetchOtDrilldown.mockResolvedValue({ ...DRILLDOWN, total: 150, omitidos: 148 });
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByText("Decididos hoy")).toBeInTheDocument());
    await user.click(screen.getByText("Decididos hoy"));

    await waitFor(() =>
      expect(screen.getByText(/148 quedaron fuera por el tope de filas/)).toBeInTheDocument(),
    );
  });

  it("cierra el drill-down y permite abrir otro bloque distinto", async () => {
    const user = userEvent.setup();
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByText("Entregados hoy")).toBeInTheDocument());
    await user.click(screen.getByText("Entregados hoy"));
    await waitFor(() => expect(screen.getByTestId("ot-reports-drilldown")).toBeInTheDocument());

    await user.click(screen.getByLabelText("Cerrar"));
    await waitFor(() =>
      expect(screen.queryByTestId("ot-reports-drilldown")).not.toBeInTheDocument(),
    );

    expect(mocks.fetchOtDrilldown).toHaveBeenLastCalledWith(expect.anything(), "entregados_hoy");
  });
});

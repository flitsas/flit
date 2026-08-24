// Reportes del organismo de tránsito. Antes de esto la pantalla era un cartel de "en
// construcción": el organismo trabajaba dentro de FLIT sin ningún instrumento propio.
//
// La consola está partida en tres pestañas porque responden preguntas con horizontes distintos.
// Los tests reflejan ese corte: cada bloque abre la pestaña que lo contiene, igual que el usuario.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type {
  OtClientCompanyOption,
  OtDrilldown,
  OtOperationalPanel,
  OtPerformance,
  OtRejectionReasons,
  OtReport,
} from "@/lib/api/ot-metrics";

const mocks = vi.hoisted(() => ({
  fetchOtOperationalPanel: vi.fn(),
  fetchOtPerformance: vi.fn(),
  fetchOtRejectionReasons: vi.fn(),
  fetchOtClientCompanies: vi.fn(),
  fetchOtDrilldown: vi.fn(),
  fetchOtReport: vi.fn(),
  // La consola pide el catálogo de revisores al montar; sin mock saldría una petición real.
  fetchOtReviewerOptions: vi.fn(),
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
    fetchOtReport: mocks.fetchOtReport,
    fetchOtReviewerOptions: mocks.fetchOtReviewerOptions,
  };
});

import { OtReportsConsole } from "@/components/admin/transit-offices/OtReportsConsole";
import {
  buildReportCsv,
  buildReportXlsx,
} from "@/components/admin/transit-offices/_reportes/report-columns";
import { buildTrendData } from "@/components/admin/transit-offices/_reportes/report-visuals";

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
      familia: "MATRICULAS",
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
      familia: "MATRICULAS",
      prioritario: true,
      diasEsperando: null,
    },
  ],
};

// Desglose que SUMA el total (5+2+3+18+4+0+0 = 32). Es el invariante del informe y la diferencia
// deliberada con el panel operativo, cuyo desglose no cierra.
const REPORT: OtReport = {
  resumen: {
    total: 32,
    enRevision: 5,
    esperandoPlaca: 2,
    esperandoCliente: 3,
    aprobados: 18,
    enSubsanacion: 4,
    rechazados: 0,
    anulados: 0,
    otros: 0,
    decididos: 22,
    devoluciones: 6,
    devolucionesPromedio: 0.19,
    tiempoMedianoHoras: 7.5,
    tiempoPromedioHoras: 19.2,
    tiempoP90Horas: 61,
    tiempoMedianoAprobacionHoras: 6.1,
    distribucionTiempos: [
      { key: "h_0_24", label: "Menos de 1 día", tramites: 14 },
      { key: "d_1_2", label: "1 a 2 días", tramites: 5 },
      { key: "d_3_5", label: "3 a 5 días", tramites: 3 },
      { key: "d_6_10", label: "6 a 10 días", tramites: 0 },
      { key: "d_mas_10", label: "Más de 10 días", tramites: 0 },
    ],
    granularidad: "dia",
    serie: [
      { bucket: "2026-08-01", label: "01 ago", desde: "2026-08-01", hasta: "2026-08-01", radicados: 12, aprobados: 8, rechazados: 2 },
      { bucket: "2026-08-02", label: "02 ago", desde: "2026-08-02", hasta: "2026-08-02", radicados: 0, aprobados: 0, rechazados: 0 },
      { bucket: "2026-08-03", label: "03 ago", desde: "2026-08-03", hasta: "2026-08-03", radicados: 20, aprobados: 10, rechazados: 2 },
    ],
  },
  total: 32,
  page: 1,
  pageSize: 25,
  filas: [
    {
      procedureInstanceId: "p1",
      referenceNumber: "REF-100",
      placa: "XYZ987",
      vin: "9BWZZZ377VT004251",
      clientTenantId: "c1",
      clientTenantName: "Distribuidora del Valle S.A.S.",
      familia: "MATRICULAS",
      status: "aprobado",
      estadoOt: "aprobado",
      prioritario: false,
      subsanacionActiva: false,
      radicadoEn: "2026-08-01T14:00:00Z",
      ultimaRadicacionEn: "2026-08-01T14:00:00Z",
      decididoEn: "2026-08-02T09:00:00Z",
      decididoPor: "Carla Revisora",
      horasHastaDecision: 19,
      diasEnOrganismo: 0.8,
      devoluciones: 0,
      causalesUltimoRechazo: [],
    },
    {
      procedureInstanceId: "p2",
      referenceNumber: "REF-101",
      placa: null,
      vin: null,
      clientTenantId: "c2",
      clientTenantName: "Comercializadora Andina Ltda.",
      familia: "TRASPASO",
      status: "rechazado",
      estadoOt: "en_subsanacion",
      prioritario: true,
      subsanacionActiva: true,
      radicadoEn: "2026-08-01T15:00:00Z",
      ultimaRadicacionEn: "2026-08-01T15:00:00Z",
      decididoEn: "2026-08-03T10:00:00Z",
      decididoPor: "Carla Revisora",
      horasHastaDecision: 43,
      diasEnOrganismo: 1.8,
      devoluciones: 2,
      causalesUltimoRechazo: ["Improntas están borrosas"],
    },
  ],
};

async function openTab(name: RegExp) {
  const user = userEvent.setup();
  await user.click(await screen.findByRole("tab", { name }));
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
  // La consola guarda la pestaña activa en `?tab=`, y jsdom conserva la dirección entre pruebas del
  // mismo archivo: sin esto, una prueba que cambia de pestaña abriría la siguiente en esa misma.
  window.history.replaceState(null, "", "/");
  mocks.fetchOtOperationalPanel.mockResolvedValue(PANEL);
  mocks.fetchOtPerformance.mockResolvedValue(PERFORMANCE);
  mocks.fetchOtRejectionReasons.mockResolvedValue(REASONS);
  mocks.fetchOtClientCompanies.mockResolvedValue(COMPANIES);
  mocks.fetchOtDrilldown.mockResolvedValue(DRILLDOWN);
  mocks.fetchOtReport.mockResolvedValue(REPORT);
  mocks.fetchOtReviewerOptions.mockResolvedValue([]);
});

// ── La pestaña activa vive en la dirección ────────────────────────────────────

describe("Reportes del organismo — pestaña en la dirección", () => {
  it("recarga en la pestaña donde estaba, no en la primera", async () => {
    const { unmount } = render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Revisores/);

    // Cambiar de pestaña deja rastro en la dirección; recargar es volver a montar sobre ella.
    expect(new URL(window.location.href).searchParams.get("tab")).toBe("revisores");
    unmount();

    render(<OtReportsConsole transitOfficeId="ot-1" />);
    expect(await screen.findByRole("tab", { name: /Revisores/, selected: true })).toBeInTheDocument();
  });

  it("abre la pestaña que pide el enlace", async () => {
    // Es el caso que hacía inútil el enlace de «Consultas»: la consulta llegaba dentro de `?q=`,
    // pero la leía un componente que se quedaba sin montar porque mandaba la pestaña por defecto.
    window.history.replaceState(null, "", "/?tab=analisis");
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    expect(await screen.findByRole("tab", { name: /Análisis/, selected: true })).toBeInTheDocument();
  });

  it("cae en la primera pestaña si el enlace nombra una que no existe", async () => {
    window.history.replaceState(null, "", "/?tab=inventada");
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    // Un enlace viejo no puede dejar la consola en blanco.
    expect(await screen.findByRole("tab", { name: /Ahora mismo/, selected: true })).toBeInTheDocument();
  });

  it("no empuja una entrada al historial por cada pestaña", async () => {
    const antes = window.history.length;
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Análisis/);
    await openTab(/Informe/);
    await openTab(/Revisores/);

    // Con `pushState`, salir de reportes costaría tantos «atrás» como pestañas se hayan mirado.
    expect(window.history.length).toBe(antes);
  });
});

// ── Pestaña «Ahora mismo» ─────────────────────────────────────────────────────

describe("Reportes del organismo — Ahora mismo", () => {
  it("abre en esta pestaña: lo primero que necesita un organismo es su cola", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    expect(await screen.findByTestId("ot-now-tab")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Ahora mismo" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("no ofrece rango de fechas, porque la cola describe el ahora y no lo filtraría", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() => expect(screen.getByTestId("ot-reports-operational")).toBeInTheDocument());

    // El control existía y no cambiaba un solo número del panel: una promesa falsa.
    expect(screen.queryByLabelText("Desde")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Hasta")).not.toBeInTheDocument();
  });

  it("declara la ventana de la mediana en vez de dejar creer que la eligió el usuario", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    await waitFor(() =>
      expect(screen.getByText("Tiempo mediano de decisión")).toBeInTheDocument(),
    );
    expect(screen.getByText("Últimos 30 días")).toBeInTheDocument();
  });

  it("formatea la mediana en vez de interpolar el número crudo", async () => {
    mocks.fetchOtOperationalPanel.mockResolvedValue({
      ...PANEL,
      movimiento: { ...PANEL.movimiento, tiempoMedianoDecisionHoras: 0.03 },
    });
    render(<OtReportsConsole transitOfficeId="ot-1" />);

    // Con datos reales esta mediana salía como «0.03 h»: punto decimal inglés y una unidad en la
    // que el dato no dice nada.
    expect(await screen.findByText("2 min")).toBeInTheDocument();
    expect(screen.queryByText("0.03 h")).not.toBeInTheDocument();
  });

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

    const links = screen.getAllByRole("link", { name: /Ir a gestionar/ });
    expect(links[0]).toHaveAttribute(
      "href",
      "/admin/transit-offices/ot-1/client-procedures?placa=ABC123&status=aprobado",
    );
    expect(links[1]).toHaveAttribute(
      "href",
      "/admin/transit-offices/ot-1/client-procedures?vin=9BWZZZ377VT004251&status=rechazado",
    );

    // En pestaña nueva: el drill-down se abre sobre un reporte con filtros puestos y suele traer
    // varios trámites que atender. Navegar en la misma pestaña obliga a rearmarlo por cada uno.
    expect(links[0]).toHaveAttribute("target", "_blank");
    // `noopener` no es adorno: sin él la pestaña destino puede manipular la de origen.
    expect(links[0]).toHaveAttribute("rel", expect.stringContaining("noopener"));
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

// ── Pestaña «Análisis» ────────────────────────────────────────────────────────

describe("Reportes del organismo — Análisis", () => {
  it("aquí sí hay rango de fechas, porque todo lo de esta pestaña depende de él", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Análisis/);

    expect(await screen.findByLabelText("Desde")).toBeInTheDocument();
    expect(screen.getByLabelText("Hasta")).toBeInTheDocument();
  });

  it("rotula los motivos como % de rechazos que incluyen la causal, no como reparto", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Análisis/);

    await waitFor(() => expect(screen.getByTestId("ot-reports-reasons")).toBeInTheDocument());
    expect(screen.getByText("Improntas mal cargadas")).toBeInTheDocument();
    // La aclaración es parte del dato: leído como reparto, el porcentaje engaña.
    expect(screen.getByText(/la suma puede pasar del 100 %/)).toBeInTheDocument();
  });

  it("expone el promedio de causales por rechazo como indicador de salud", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Análisis/);

    // Si este número se acerca al tamaño del catálogo, alguien está marcando todo.
    await waitFor(() =>
      expect(screen.getByText("Causales por rechazo (promedio)")).toBeInTheDocument(),
    );
    expect(screen.getByText("1.8")).toBeInTheDocument();
  });

  it("ya no duplica el desempeño de las personas, que vive en su propia pestaña", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Análisis/);

    await waitFor(() => expect(screen.getByTestId("ot-reports-companies")).toBeInTheDocument());

    // La tabla de revisores se MUDÓ a «Revisores», que además filtra por persona y exporta.
    // Mantener una copia aquí habría dejado los mismos números en dos sitios con filtros
    // distintos, y en cuanto difieren el reporte deja de merecer confianza.
    expect(screen.queryByTestId("ot-reports-reviewers")).not.toBeInTheDocument();
    expect(screen.queryByText("Carla Revisora")).not.toBeInTheDocument();
  });

  it("no muestra un porcentaje sin base para empresas sin nada decidido", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Análisis/);

    // «100 %» sobre cero aprobados se leería como una empresa impecable; el dato correcto es «—».
    const fila = (await screen.findByText("Renting Sin Actividad S.A.")).closest("tr");
    expect(fila).not.toBeNull();
    expect(fila!.textContent).toContain("—");
  });

  it("un atajo de rango recarga el análisis con el rango elegido", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Análisis/);

    await waitFor(() => expect(mocks.fetchOtRejectionReasons).toHaveBeenCalled());
    mocks.fetchOtRejectionReasons.mockClear();

    await user.click(screen.getByRole("button", { name: "Últimos 7 días" }));

    await waitFor(() => expect(mocks.fetchOtRejectionReasons).toHaveBeenCalled());
    const [params] = mocks.fetchOtRejectionReasons.mock.calls.at(-1)!;
    const dias =
      (Date.parse(params.to) - Date.parse(params.from)) / (1000 * 60 * 60 * 24) + 1;
    expect(dias).toBe(7);
  });
});

// ── Pestaña «Informe» ─────────────────────────────────────────────────────────

describe("Reportes del organismo — Informe", () => {
  it("el desglose por estado suma el total, a diferencia del panel operativo", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-resumen")).toBeInTheDocument());

    const resumen = screen.getByTestId("ot-report-resumen");
    expect(within(resumen).getByText("Trámites recibidos")).toBeInTheDocument();
    expect(within(resumen).getByText("32")).toBeInTheDocument();
    expect(
      within(resumen).getByText(/el desglose suma el total/i),
    ).toBeInTheDocument();

    // La barra apilada es lo que hace visible el invariante.
    expect(screen.getByTestId("ot-report-composicion")).toBeInTheDocument();
  });

  it("no mezcla borradores ni preparados: el organismo nunca los vio", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-filters")).toBeInTheDocument());
    expect(screen.getByText(/Los estados previos a la radicación no aparecen/)).toBeInTheDocument();
    expect(screen.queryByText("Borrador")).not.toBeInTheDocument();
    expect(screen.queryByText("Preparado")).not.toBeInTheDocument();
  });

  it("acompaña la mediana con p90 y con el histograma, para que la cola no quede escondida", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-tiempos")).toBeInTheDocument());
    expect(screen.getByText("Mediana (p50)")).toBeInTheDocument();
    expect(screen.getByText("p90")).toBeInTheDocument();
    expect(screen.getByTestId("ot-report-histograma")).toBeInTheDocument();
    // Dice sobre cuántos se calculó: un tiempo sin denominador no se puede defender.
    expect(
      screen.getByText(/Calculado sobre 22 de 32 trámites recibidos con decisión/),
    ).toBeInTheDocument();
  });

  it("expone los valores de la serie en texto, incluidos los periodos vacíos", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    // El SVG de la gráfica no se puede copiar ni lo lee un lector de pantalla: la tabla es el mismo
    // dato en texto. Los tres periodos están, aunque el del medio esté en cero — un hueco omitido
    // se leería como continuidad.
    const valores = await screen.findByTestId("ot-report-tendencia-valores");
    expect(within(valores).getByText("01 ago")).toBeInTheDocument();
    expect(within(valores).getByText("02 ago")).toBeInTheDocument();
    expect(within(valores).getByText("03 ago")).toBeInTheDocument();
  });

  it("dice en texto si la cola creció, sin obligar a leer la gráfica", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    // 32 radicados contra 22 decisiones: la respuesta a «¿voy al ritmo?» es que no, y eso no puede
    // depender de pasar el ratón por encima de una línea.
    const saldo = await screen.findByTestId("ot-report-tendencia-saldo");
    expect(saldo).toHaveTextContent("Se acumularon 10 sin decidir");
  });

  it("permite elegir columnas y la tabla responde sin volver a consultar", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-table")).toBeInTheDocument());
    expect(screen.getByRole("columnheader", { name: /Placa/ })).toBeInTheDocument();

    mocks.fetchOtReport.mockClear();
    await user.click(screen.getByTestId("ot-report-column-picker"));
    await user.click(await screen.findByLabelText("Placa"));

    await waitFor(() =>
      expect(screen.queryByRole("columnheader", { name: /Placa/ })).not.toBeInTheDocument(),
    );
    // El backend devuelve todos los campos de cada fila: marcar columnas no puede costar una consulta.
    expect(mocks.fetchOtReport).not.toHaveBeenCalled();
  });

  it("una vista rápida reconfigura las columnas de golpe", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-table")).toBeInTheDocument());
    expect(screen.queryByRole("columnheader", { name: /Decidido por/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Tiempos de respuesta" }));

    expect(
      await screen.findByRole("columnheader", { name: /Decidido por/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: /Tiempo de decisión/ })).toBeInTheDocument();
  });

  it("ordenar por una columna vuelve a pedir el informe al backend", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-table")).toBeInTheDocument());
    mocks.fetchOtReport.mockClear();

    await user.click(screen.getByRole("button", { name: "Ordenar por Devoluciones" }));

    // El orden es del universo completo, no de la página: tiene que resolverlo el servidor.
    await waitFor(() =>
      expect(mocks.fetchOtReport).toHaveBeenCalledWith(
        expect.objectContaining({ sortBy: "devoluciones" }),
        expect.anything(),
      ),
    );
  });

  it("el resumen describe el universo aunque la tabla muestre una página", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-tabla")).toBeInTheDocument());
    // 32 trámites en total con 2 filas visibles: el pie lo dice explícitamente.
    expect(screen.getByText(/32 trámites · página 1 de 2/)).toBeInTheDocument();
  });

  it("marca el estado con su etiqueta del organismo, no con el estado crudo", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    const tabla = await screen.findByTestId("ot-report-table");
    // `rechazado` + subsanación abierta no es lo mismo que un rechazo cerrado: vuelve.
    expect(within(tabla).getByText("En subsanación")).toBeInTheDocument();
    expect(within(tabla).getByText("Aprobado")).toBeInTheDocument();
    // El estado crudo del trámite no se filtra a la pantalla.
    expect(within(tabla).queryByText("en_subsanacion")).not.toBeInTheDocument();
  });

  it("exporta el informe completo, no solo la página visible", async () => {
    const createObjectURL = vi.fn(() => "blob:informe");
    const revokeObjectURL = vi.fn();
    vi.stubGlobal("URL", { ...URL, createObjectURL, revokeObjectURL });

    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-export-xlsx")).toBeEnabled());
    mocks.fetchOtReport.mockResolvedValue({ ...REPORT, total: 2, pageSize: 200 });

    await user.click(screen.getByTestId("ot-report-export-xlsx"));

    // Un archivo con 25 filas cuando la pantalla dice «32 trámites» es una trampa silenciosa.
    await waitFor(() =>
      expect(mocks.fetchOtReport).toHaveBeenCalledWith(
        expect.objectContaining({ pageSize: 200 }),
      ),
    );
    await waitFor(() => expect(createObjectURL).toHaveBeenCalled());
    expect(await screen.findByText(/Se exportaron 2 filas/)).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  it("marca cuál vista rápida se está mirando", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    // Las columnas por defecto no son ninguna de las vistas: decirlo evita que el usuario crea que
    // está exportando «Gestión» cuando está exportando otra cosa.
    expect(await screen.findByTestId("ot-report-preset-personalizada")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Gestión" }));

    expect(screen.getByRole("button", { name: "Gestión" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(screen.getByRole("button", { name: "Calidad de lo que llega" })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
    expect(screen.queryByTestId("ot-report-preset-personalizada")).not.toBeInTheDocument();
  });

  it("pinchar un periodo de la gráfica acota el informe a esos días y se puede deshacer", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    await waitFor(() => expect(screen.getByTestId("ot-report-tendencia")).toBeInTheDocument());
    const desde = (mocks.fetchOtReport.mock.calls.at(-1)?.[0] as { from: string }).from;
    mocks.fetchOtReport.mockClear();

    await user.click(screen.getByRole("button", { name: "Acotar el informe a 03 ago" }));

    // Los límites del periodo vienen del backend: derivarlos aquí duplicaría la regla de semanas.
    await waitFor(() =>
      expect(mocks.fetchOtReport).toHaveBeenCalledWith(
        expect.objectContaining({ from: "2026-08-03", to: "2026-08-03" }),
        expect.anything(),
      ),
    );
    expect(screen.getByTestId("ot-report-zoom")).toHaveTextContent("03 ago");

    await user.click(screen.getByRole("button", { name: "Volver al rango anterior" }));

    // Volver devuelve el rango que había, no un rango por defecto: si no, el zoom sería una vía sin
    // retorno para quien había escrito dos fechas a mano.
    await waitFor(() =>
      expect(mocks.fetchOtReport).toHaveBeenCalledWith(
        expect.objectContaining({ from: desde }),
        expect.anything(),
      ),
    );
    expect(screen.queryByTestId("ot-report-zoom")).not.toBeInTheDocument();
  });

  it("dice cuándo no hay nada, en vez de dejar una tabla vacía sin explicación", async () => {
    mocks.fetchOtReport.mockResolvedValue({
      ...REPORT,
      resumen: { ...REPORT.resumen, total: 0, decididos: 0 },
      total: 0,
      filas: [],
    });

    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    expect(
      await screen.findByText(/Ningún trámite fue recibido por el organismo en el periodo/),
    ).toBeInTheDocument();
  });
});

// ── Exportación ───────────────────────────────────────────────────────────────

describe("Descarga del informe", () => {
  it("no ofrece la descarga en CSV", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    // Oculta bajo CSV_EXPORT_VISIBLE, no eliminada: el generador sigue probado más abajo.
    expect(await screen.findByTestId("ot-report-export-xlsx")).toBeInTheDocument();
    expect(screen.queryByTestId("ot-report-export")).not.toBeInTheDocument();
  });
});

describe("CSV del informe", () => {
  it("exporta exactamente las columnas visibles y en su orden", () => {
    const csv = buildReportCsv(REPORT.filas, ["empresa", "referencia", "devoluciones"]);

    // El BOM va delante o Excel en español rompe las tildes.
    expect(csv.startsWith("﻿")).toBe(true);

    const [header, primera] = csv.slice(1).split("\r\n");

    // El orden lo fija la definición de columnas, no el orden en que se fueron marcando.
    expect(header).toBe('"Radicado";"Empresa";"Devoluciones"');
    expect(primera).toBe('"REF-100";"Distribuidora del Valle S.A.S.";"0"');
  });

  it("el pendiente acumulado suma los saldos periodo a periodo", () => {
    const puntos = buildTrendData(REPORT.resumen.serie);

    // 12−10 = +2, luego 0, luego 20−12 = +8 → 2, 2, 10. Es la respuesta a «¿voy al ritmo?»: la
    // línea sube, o sea que entra más de lo que sale.
    expect(puntos.map((p) => p.acumulado)).toEqual([2, 2, 10]);
    expect(puntos[1].saldo).toBe(0);
  });

  it("neutraliza los valores que Excel ejecutaría como fórmula", () => {
    const csv = buildReportCsv(
      [{ ...REPORT.filas[0], referenceNumber: "=1+1" }],
      ["referencia"],
    );

    // El radicado lo escribe la empresa cliente: es una vía de inyección real, no teórica.
    expect(csv).toContain(`"'=1+1"`);
  });
});

describe("Excel del informe", () => {
  // El .xlsx se escribe sin comprimir, así que el XML de la hoja aparece literal en los bytes del
  // zip. Eso permite afirmar sobre el contenido sin descomprimir nada en el test.
  const sheetXmlOf = (columnas: string[]) =>
    new TextDecoder().decode(buildReportXlsx(REPORT.filas, columnas));

  it("escribe los números como números, no como el texto de la pantalla", () => {
    const xml = sheetXmlOf(["referencia", "devoluciones"]);

    // La razón de ser del Excel frente al CSV: esta celda se puede sumar. Si fuera `t="inlineStr"`
    // el archivo sería un CSV con otra extensión.
    expect(xml).toContain('<c r="B2" s="0"><v>0</v></c>');
    expect(xml).toContain('<c r="B3" s="0"><v>2</v></c>');
    expect(xml).toContain("REF-100");
  });

  it("escribe las fechas como fechas, en el día de Bogotá", () => {
    const xml = sheetXmlOf(["radicado_en"]);

    // 01/08/2026 14:00 UTC son las 09:00 en Bogotá: el mismo día que muestra la pantalla. Con el
    // instante UTC crudo, un trámite de las 22:00 saltaría al día siguiente solo en el Excel.
    const serial = (Date.UTC(2026, 7, 1) - Date.UTC(1899, 11, 30)) / 86_400_000;
    expect(xml).toContain(`<c r="A2" s="2"><v>${serial}</v></c>`);
  });

  it("lleva la unidad al encabezado cuando la celda exporta el número desnudo", () => {
    const xml = sheetXmlOf(["horas_decision"]);

    // En pantalla la celda dice «19 h»; en la hoja dice «19». Sin la unidad arriba, el número no
    // significa nada.
    expect(xml).toContain("Tiempo de decisión (h)");
    expect(xml).toContain('<c r="A2" s="0"><v>19</v></c>');
  });

  it("deja vacías las celdas sin dato en vez de escribir un guion", () => {
    const sinDecision = { ...REPORT.filas[0], horasHastaDecision: null };
    const xml = new TextDecoder().decode(buildReportXlsx([sinDecision], ["horas_decision"]));

    // Un «—» en una columna numérica la vuelve texto para toda la hoja y rompe cualquier suma.
    expect(xml).toContain('<c r="A2"/>');
    expect(xml).not.toContain("—");
  });

  it("produce un zip que empieza por la firma de archivo local", () => {
    const bytes = buildReportXlsx(REPORT.filas, ["referencia"]);

    // Sin esta firma Excel ni siquiera intenta abrirlo: es el contrato mínimo del formato.
    expect([bytes[0], bytes[1], bytes[2], bytes[3]]).toEqual([0x50, 0x4b, 0x03, 0x04]);
  });
});

// ── Filtro por familia ────────────────────────────────────────────────────────

describe("Reportes del organismo — filtro por familia", () => {
  // El selector ofrecía «Matrícula inicial» y «Traspaso», valores de un vocabulario que ADR-0050
  // eliminó, contra una consulta que compara con `procedure_types.family`. Ninguno coincidía: elegir
  // una opción vaciaba el informe sin decir por qué. Y faltaba «Otros», donde viven diecisiete de
  // los veintiún tipos del catálogo.
  it("ofrece las tres familias del catálogo", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openTab(/Informe/);

    const select = await screen.findByLabelText("Familia");
    const opciones = within(select).getAllByRole("option").map((o) => o.textContent);

    expect(opciones).toEqual(["Todas las familias", "Matrículas", "Traspaso", "Otros trámites"]);
  });

  it("envía el código de familia que la consulta sabe comparar", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openTab(/Informe/);

    await user.selectOptions(await screen.findByLabelText("Familia"), "MATRICULAS");

    await waitFor(() =>
      expect(mocks.fetchOtReport).toHaveBeenCalledWith(
        expect.objectContaining({ family: "MATRICULAS" }),
        expect.anything(),
      ),
    );
  });
});


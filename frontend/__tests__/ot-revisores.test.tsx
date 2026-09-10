// Informe de revisores del organismo.
//
// El sesgo que se prueba aquí es deliberado: este informe habla de PERSONAS, así que lo que no puede
// pasar es que el volumen aparezca sin contexto. Varios de estos tests existen solo para fijar eso.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type {
  OtClientCompanyOption,
  OtReviewerOption,
  OtReviewersReport,
} from "@/lib/api/ot-metrics";

const mocks = vi.hoisted(() => ({
  fetchOtOperationalPanel: vi.fn(),
  fetchOtPerformance: vi.fn(),
  fetchOtRejectionReasons: vi.fn(),
  fetchOtClientCompanies: vi.fn(),
  fetchOtDrilldown: vi.fn(),
  fetchOtReport: vi.fn(),
  fetchOtReviewers: vi.fn(),
  fetchOtReviewerOptions: vi.fn(),
}));

vi.mock("@/lib/api/ot-metrics", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ot-metrics")>();
  return { ...actual, ...mocks };
});

import { OtReportsConsole } from "@/components/admin/transit-offices/OtReportsConsole";
import {
  buildReviewersCsv,
  buildReviewersXlsx,
} from "@/components/admin/transit-offices/_reportes/reviewer-columns";

const COMPANIES: OtClientCompanyOption[] = [
  { tenantId: "c1", name: "Distribuidora del Valle S.A.S." },
];

const REVIEWERS: OtReviewerOption[] = [
  { userId: "u1", displayName: "Carla Revisora", decisiones: 412 },
  { userId: "u2", displayName: "Diego Revisor", decisiones: 96 },
  { userId: "u3", displayName: "Elena Sin Actividad", decisiones: 3 },
];

// Carla concentra 100 de 130 decisiones: el caso que obliga a decir en voz alta que el equipo
// depende de una sola persona.
const REPORT: OtReviewersReport = {
  resumen: {
    revisores: 2,
    decididos: 130,
    aprobados: 104,
    rechazados: 26,
    aprobacionPct: 80,
    tiempoMedianoHoras: 5.5,
    tiempoP90Horas: 40,
    concentracionTopPct: 76.9,
    revisorMasActivo: "Carla Revisora",
  },
  filas: [
    {
      userId: "u1",
      displayName: "Carla Revisora",
      decididos: 100,
      aprobados: 82,
      aprobacionPct: 82,
      rechazados: 18,
      rechazoPct: 18,
      tiempoMedianoHoras: 4,
      tiempoPromedioHoras: 9.5,
      tiempoP90Horas: 30,
      tiempoMaximoHoras: 120,
      enMenosDe24hPct: 91,
      vuelvenARechazarsePct: 11,
      causalesPorRechazo: 1.4,
      diasActivos: 20,
      decisionesPorDiaActivo: 5,
      empresasAtendidas: 4,
      prioritariosDecididos: 7,
      primeraDecision: "2026-07-08T14:00:00Z",
      ultimaDecision: "2026-08-04T22:30:00Z",
    },
    {
      userId: "u2",
      displayName: "Diego Revisor",
      decididos: 30,
      aprobados: 22,
      aprobacionPct: 73.3,
      rechazados: 0,
      rechazoPct: 0,
      tiempoMedianoHoras: 0.05,
      tiempoPromedioHoras: 0.1,
      tiempoP90Horas: 2,
      tiempoMaximoHoras: 3,
      enMenosDe24hPct: 100,
      vuelvenARechazarsePct: 0,
      causalesPorRechazo: 0,
      diasActivos: 12,
      decisionesPorDiaActivo: 2.5,
      empresasAtendidas: 2,
      prioritariosDecididos: 0,
      primeraDecision: "2026-07-20T14:00:00Z",
      ultimaDecision: "2026-08-01T15:00:00Z",
    },
  ],
};

async function openRevisores() {
  const user = userEvent.setup();
  await user.click(await screen.findByRole("tab", { name: /Revisores/ }));
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
  // Ver ot-reportes: la pestaña activa viaja en `?tab=` y jsdom no reinicia la dirección.
  window.history.replaceState(null, "", "/");
  mocks.fetchOtOperationalPanel.mockResolvedValue({
    movimiento: { entregadosHoy: 0, decididosHoy: 0, pendientesTotal: 0, tiempoMedianoDecisionHoras: null },
    cola: { porRevisar: 0, esperandoAsignarPlaca: 0, enEsperaDelCliente: 0 },
    antiguedad: { hasta1Dia: 0, entre2y3Dias: 0, entre4y7Dias: 0, masDe7Dias: 0, prioritariosEstancados: 0 },
  });
  mocks.fetchOtClientCompanies.mockResolvedValue(COMPANIES);
  mocks.fetchOtReviewerOptions.mockResolvedValue(REVIEWERS);
  mocks.fetchOtReviewers.mockResolvedValue(REPORT);
});

describe("Reportes del organismo — Revisores", () => {
  it("es una pestaña propia y no una tabla más dentro de Análisis", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    expect(await screen.findByTestId("ot-reviewers-tab")).toBeInTheDocument();

    // La tabla se MUDÓ: dejarla también en Análisis pondría los mismos números en dos sitios con
    // filtros distintos, y en cuanto difieren el reporte deja de merecer confianza.
    expect(screen.queryByTestId("ot-reports-reviewers")).not.toBeInTheDocument();
  });

  it("arranca con todos los revisores, porque un informe vacío al abrirlo no sirve", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    expect(await screen.findByTestId("ot-reviewers-picker")).toHaveTextContent("Todos los revisores");

    // Sin selección NO se manda userIds: el backend lee esa ausencia como «todos».
    await waitFor(() => expect(mocks.fetchOtReviewers).toHaveBeenCalled());
    const params = mocks.fetchOtReviewers.mock.calls[0][0] as { userIds: string[] };
    expect(params.userIds).toEqual([]);
  });

  it("permite elegir varios revisores y vuelve a consultar con la selección", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openRevisores();

    await waitFor(() => expect(mocks.fetchOtReviewers).toHaveBeenCalled());
    await user.click(screen.getByTestId("ot-reviewers-picker"));

    const panel = screen.getByTestId("ot-reviewers-picker-panel");
    await user.click(within(panel).getByRole("checkbox", { name: /Carla Revisora/ }));
    await user.click(within(panel).getByRole("checkbox", { name: /Diego Revisor/ }));

    // El filtro llega al servidor: el cálculo de porcentajes y medianas es suyo, no de la pantalla.
    await waitFor(() =>
      expect(mocks.fetchOtReviewers).toHaveBeenCalledWith(
        expect.objectContaining({ userIds: ["u1", "u2"] }),
        expect.anything(),
      ),
    );
    expect(screen.getByTestId("ot-reviewers-picker")).toHaveTextContent("2 revisores");
  });

  it("vuelve a «todos» de un clic en vez de obligar a desmarcar uno por uno", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openRevisores();

    await user.click(await screen.findByTestId("ot-reviewers-picker"));
    const panel = screen.getByTestId("ot-reviewers-picker-panel");
    await user.click(within(panel).getByRole("checkbox", { name: /Carla Revisora/ }));
    expect(screen.getByTestId("ot-reviewers-picker")).toHaveTextContent("Carla Revisora");

    await user.click(within(panel).getByRole("button", { name: "Todos" }));

    expect(screen.getByTestId("ot-reviewers-picker")).toHaveTextContent("Todos los revisores");
  });

  it("muestra los indicadores que se pidieron: volumen, aprobados, rechazos y tiempos", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    const tabla = await screen.findByTestId("ot-reviewers-table");
    const carla = within(tabla).getByText("Carla Revisora").closest("tr")!;

    expect(within(carla).getByText("100")).toBeInTheDocument(); // gestionados
    expect(within(carla).getByText("82")).toBeInTheDocument(); // aprobados
    expect(within(carla).getByText("18")).toBeInTheDocument(); // rechazados
    expect(within(carla).getByText("4 h")).toBeInTheDocument(); // tiempo mediano
  });

  it("formatea los tiempos en vez de volcar el número crudo", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    const tabla = await screen.findByTestId("ot-reviewers-table");
    const diego = within(tabla).getByText("Diego Revisor").closest("tr")!;

    // 0,05 h son 3 minutos. «0.05 h» es el defecto que ya apareció una vez en el panel operativo.
    expect(within(diego).getByText("3 min")).toBeInTheDocument();
    expect(within(diego).queryByText(/0\.05/)).not.toBeInTheDocument();
  });

  it("dice «—» y no «0 %» donde no hay base para calcular", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    const tabla = await screen.findByTestId("ot-reviewers-table");
    const diego = within(tabla).getByText("Diego Revisor").closest("tr")!;

    // Diego no rechazó nada: un 0 % de reincidencia se leería como calidad impecable cuando lo que
    // pasa es que no hay nada que medir.
    expect(within(diego).getByText("—")).toBeInTheDocument();
  });

  it("avisa cuando una sola persona concentra el trabajo del equipo", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    // El número solo no lo dice; la frase sí. Es lo que convierte el dato en una decisión.
    expect(
      await screen.findByText(/Carla Revisora concentra el 76,9 % de las decisiones/),
    ).toBeInTheDocument();
    expect(screen.getByText(/Si esa persona falta, la cola se detiene/)).toBeInTheDocument();
  });

  it("ordena por una columna pidiéndoselo al servidor", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openRevisores();

    await waitFor(() => expect(screen.getByTestId("ot-reviewers-table")).toBeInTheDocument());
    mocks.fetchOtReviewers.mockClear();

    await user.click(screen.getByRole("button", { name: "Ordenar por Tiempo mediano" }));

    // El orden es del conjunto, no de lo que se ve: sin paginación sigue siendo del servidor para
    // que coincida exactamente con lo que se exporta.
    await waitFor(() =>
      expect(mocks.fetchOtReviewers).toHaveBeenCalledWith(
        expect.objectContaining({ sortBy: "tiempo" }),
        expect.anything(),
      ),
    );
  });

  it("permite elegir columnas y marca la vista rápida activa", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openRevisores();

    await waitFor(() => expect(screen.getByTestId("ot-reviewers-table")).toBeInTheDocument());
    expect(screen.queryByRole("columnheader", { name: /Empresas atendidas/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Carga de trabajo" }));

    expect(screen.getByRole("button", { name: "Carga de trabajo" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(screen.getByRole("columnheader", { name: /Días activos/ })).toBeInTheDocument();
  });

  it("dice cuándo la selección no decidió nada, en vez de dejar una tabla vacía", async () => {
    mocks.fetchOtReviewers.mockResolvedValue({
      resumen: { ...REPORT.resumen, revisores: 0, decididos: 0 },
      filas: [],
    });

    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    expect(
      await screen.findByText(/Nadie decidió trámites en el periodo y con los filtros seleccionados/),
    ).toBeInTheDocument();
  });

  it("exporta a Excel lo que se está viendo", async () => {
    const createObjectURL = vi.fn(() => "blob:revisores");
    const revokeObjectURL = vi.fn();
    vi.stubGlobal("URL", { ...URL, createObjectURL, revokeObjectURL });

    render(<OtReportsConsole transitOfficeId="ot-1" />);
    const user = await openRevisores();

    await waitFor(() => expect(screen.getByTestId("ot-reviewers-export-xlsx")).toBeEnabled());
    await user.click(screen.getByTestId("ot-reviewers-export-xlsx"));

    expect(createObjectURL).toHaveBeenCalled();
    expect(await screen.findByText(/Se exportaron 2 revisores/)).toBeInTheDocument();

    vi.unstubAllGlobals();
  });
});

describe("Exportación del informe de revisores", () => {
  it("no ofrece la descarga en CSV", async () => {
    render(<OtReportsConsole transitOfficeId="ot-1" />);
    await openRevisores();

    // Oculta bajo CSV_EXPORT_VISIBLE, no eliminada: el generador sigue probado aquí abajo.
    expect(await screen.findByTestId("ot-reviewers-export-xlsx")).toBeInTheDocument();
    expect(screen.queryByTestId("ot-reviewers-export-csv")).not.toBeInTheDocument();
  });

  it("el CSV lleva las columnas visibles en el orden de la definición", () => {
    const csv = buildReviewersCsv(REPORT.filas, ["decididos", "revisor"]);

    expect(csv.startsWith("﻿")).toBe(true);
    const [header, primera] = csv.slice(1).split("\r\n");

    expect(header).toBe('"Revisor";"Trámites gestionados"');
    expect(primera).toBe('"Carla Revisora";"100"');
  });

  it("el Excel escribe los indicadores como números para poder promediarlos", () => {
    const xml = new TextDecoder().decode(
      buildReviewersXlsx(REPORT.filas, ["revisor", "decididos", "aprobacion_pct"]),
    );

    expect(xml).toContain('<c r="B2" s="0"><v>100</v></c>');
    // El porcentaje va como número (82) y la unidad la lleva el encabezado: así se puede promediar.
    expect(xml).toContain('<c r="C2" s="0"><v>82</v></c>');
    expect(xml).toContain("Carla Revisora");
  });

  it("deja vacía la reincidencia de quien no rechazó nada, en vez de escribir un cero", () => {
    const xml = new TextDecoder().decode(
      buildReviewersXlsx([REPORT.filas[1]], ["revisor", "reincidencia"]),
    );

    // Un 0 % en la hoja se promediaría con los demás y bajaría la reincidencia del equipo entero
    // por alguien que no rechazó nada.
    expect(xml).toContain('<c r="B2"/>');
  });
});

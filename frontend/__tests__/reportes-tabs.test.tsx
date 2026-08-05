// Reportes 2.0 (HU-C): pestañas por permiso, KPIs del Resumen, helper variationPct,
// auto-refresh del panel "Ahora mismo", drill-down y aviso de compañía para SuperAdmin.
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import type { AnalyticsOverviewResponse, ProcedureDetailsPage } from "@/lib/api/types";
import type {
  FunnelResponse,
  LiveOverviewResponse,
  OtMetricsResponse,
  UsageResponse,
} from "@/lib/api/analytics-v2";

// ── Mocks ────────────────────────────────────────────────────────────────────
const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchTopProducers: vi.fn(),
  fetchProcedureDetails: vi.fn(),
  exportAnalyticsExcel: vi.fn(),
  exportExecutivePdf: vi.fn(),
  fetchLiveOverview: vi.fn(),
  fetchOtMetrics: vi.fn(),
  fetchFunnel: vi.fn(),
  fetchUsageMetrics: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
  usePermissions: vi.fn(),
}));

vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: mocks.fetchAnalyticsOverview,
  fetchMonthlyTrend: mocks.fetchMonthlyTrend,
  fetchTopProducers: mocks.fetchTopProducers,
  fetchProcedureDetails: mocks.fetchProcedureDetails,
  exportAnalyticsExcel: mocks.exportAnalyticsExcel,
  exportExecutivePdf: mocks.exportExecutivePdf,
}));
// analytics-v2: se mockean los fetchers pero variationPct es el REAL (§5 del contrato).
vi.mock("@/lib/api/analytics-v2", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/analytics-v2")>();
  return {
    ...actual,
    fetchLiveOverview: mocks.fetchLiveOverview,
    fetchOtMetrics: mocks.fetchOtMetrics,
    fetchFunnel: mocks.fetchFunnel,
    fetchUsageMetrics: mocks.fetchUsageMetrics,
  };
});
vi.mock("@/lib/api/admin-companies", () => ({ fetchCompaniesIndex: mocks.fetchCompaniesIndex }));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { variationPct } from "@/lib/api/analytics-v2";
import { Reportes } from "@/components/atom/modules/Reportes";
import { LiveNowPanel } from "@/components/atom/modules/_reportes/LiveNowPanel";

// ── Datos de prueba ──────────────────────────────────────────────────────────
const OVERVIEW: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-07-01",
  to: "2026-07-07",
  categories: [
    { category: "matriculas", total: 120, byStatus: [{ status: "submitted", count: 80 }, { status: "approved", count: 40 }] },
    { category: "traspasos", total: 30, byStatus: [{ status: "submitted", count: 30 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const LIVE: LiveOverviewResponse = {
  generatedAt: "2026-07-07T14:03:22Z",
  today: { creados: 14, byStatus: [{ status: "borrador", count: 6 }], entregados: 5, aprobados: 3, rechazados: 1 },
  stuckCount: 7,
  pendingIdentityValidations: 3,
  integrationsLastHour: { calls: 25, errors: 1, avgDurationMs: 350 },
  lastActivityAt: "2026-07-07T13:59:01Z",
};

const FUNNEL: FunnelResponse = {
  current: {
    states: [
      { stage: "borrador", count: 200, pctOfFirst: 100, pctOfPrev: 100 },
      { stage: "preparado", count: 150, pctOfFirst: 75, pctOfPrev: 75 },
      { stage: "entregado", count: 120, pctOfFirst: 60, pctOfPrev: 80 },
      { stage: "aprobado", count: 90, pctOfFirst: 45, pctOfPrev: 75 },
    ],
    anulados: 12,
    rechazadosVigentes: 18,
    wizardSteps: [],
  },
  previous: null,
  comparison: null,
};

const OT_METRICS: OtMetricsResponse = {
  current: {
    summary: {
      entregados: 120,
      aprobados: 90,
      rechazados: 18,
      rejectionRatePct: 16.7,
      avgApprovalHours: 52.4,
      p50ApprovalHours: 41,
      p90ApprovalHours: 130,
      reincidencePct: 61.1,
      stuckCount: 7,
    },
    rejectionByOffice: [
      { transitOfficeId: "ot1", transitOfficeName: "OT Bogotá", entregados: 40, aprobados: 30, rechazados: 8, rejectionRatePct: 21.1 },
    ],
    rejectionByReason: [{ reason: "Documento ilegible", count: 9, pct: 50 }],
    rejectionByType: [
      { procedureTypeId: "pt1", procedureTypeName: "Traspaso", entregados: 60, rechazados: 10, rejectionRatePct: 14.3 },
    ],
    approvalTimesByOffice: [
      { transitOfficeId: "ot1", transitOfficeName: "OT Bogotá", decididos: 38, avgHours: 50.1, p50Hours: 40, p90Hours: 120.5 },
    ],
    officeRanking: [
      { transitOfficeId: "ot1", transitOfficeName: "OT Bogotá", rank: 1, p50Hours: 24, rejectionRatePct: 5, volumen: 40 },
    ],
    reincidence: { rechazadas: 18, reintentadas: 11, avgCiclos: 1.4, maxCiclos: 3 },
    stuck: {
      totalCount: 1,
      items: [
        {
          instanceId: "inst-1",
          referenceNumber: "TRM-2026-000001",
          status: "entregado",
          daysInStatus: 12.3,
          transitOfficeName: "OT Bogotá",
          procedureTypeName: "Traspaso",
          createdByDisplayName: "Ana Pérez",
        },
      ],
    },
    // Causales tipificadas: los porcentajes NO suman 100 % a propósito — un rechazo puede llevar
    // varias causales, y la vista lo rotula así.
    rejectionByReasonCatalog: [
      { reasonId: "rr1", code: "soat_no_vigente", description: "SOAT no vigente", rechazos: 12, pct: 66.7 },
      { reasonId: "rr2", code: "improntas_borrosas", description: "Improntas están borrosas", rechazos: 7, pct: 38.9 },
    ],
    avgReasonsPerRejection: 1.06,
    internalCycle: { avgHours: 38.5, p50Hours: 26, p90Hours: 96 },
  },
  previous: null,
  comparison: null,
};

const USAGE_EMPTY: UsageResponse = {
  current: {
    moduleUsage: [],
    wizardSteps: [],
    peakHours: [],
    documentReplacements: [],
    externalApis: [],
    avgWizardDurationMs: null,
    medianWizardDurationMs: null,
  },
  previous: null,
  comparison: null,
};

const DETAIL_PAGE: ProcedureDetailsPage = {
  items: [
    {
      id: "p1",
      referenceNumber: "TRM-2026-000008",
      procedureTypeName: "Traspaso de vehículo",
      category: "traspasos",
      status: "preparado",
      createdByDisplayName: "Ana Pérez",
      submittedAt: null,
      completedAt: null,
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 10,
};

const ALL_SLUGS = [
  "reportes.resumen.read",
  "reportes.operacion.read",
  "reportes.ot.read",
  "reportes.uso.read",
  "reportes.productividad.read",
];

function setPermissions(permissions: string[], isSuperAdmin = false) {
  mocks.usePermissions.mockReturnValue({
    permissions,
    isSuperAdmin,
    isAdminCompany: false,
    isOtAdmin: false,
    tenantId: null,
    userId: null,
    roleId: null,
    roleCode: null,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  setPermissions(ALL_SLUGS);
  mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
  mocks.fetchMonthlyTrend.mockResolvedValue({ items: [] });
  mocks.fetchTopProducers.mockResolvedValue({ items: [] });
  mocks.fetchProcedureDetails.mockResolvedValue(DETAIL_PAGE);
  mocks.fetchLiveOverview.mockResolvedValue(LIVE);
  mocks.fetchOtMetrics.mockResolvedValue(OT_METRICS);
  mocks.fetchFunnel.mockResolvedValue(FUNNEL);
  mocks.fetchUsageMetrics.mockResolvedValue(USAGE_EMPTY);
  mocks.fetchCompaniesIndex.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 100 });
  window.history.replaceState(null, "", "/");
});

afterEach(() => {
  vi.useRealTimers();
});

// ── §5: helper único de variación ────────────────────────────────────────────
describe("variationPct — helper único de variación (§5)", () => {
  it("calcula la variación % con 1 decimal", () => {
    expect(variationPct(110, 100)).toBe(10);
    expect(variationPct(90, 100)).toBe(-10);
    expect(variationPct(1, 3)).toBe(-66.7);
    expect(variationPct(0, 8)).toBe(-100);
  });

  it("devuelve null sin base de comparación (previous 0 o null)", () => {
    expect(variationPct(50, 0)).toBeNull();
    expect(variationPct(50, null)).toBeNull();
    expect(variationPct(50, undefined)).toBeNull();
  });
});

// ── Visibilidad de pestañas por permiso (§3) ─────────────────────────────────
describe("Reportes — pestañas según permisos RBAC", () => {
  it("con todos los slugs muestra las 5 pestañas temáticas", async () => {
    render(<Reportes />);

    const tabs = await screen.findAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      "Resumen general",
      "Operación / Trámites",
      "Organismo de Tránsito",
      "Uso del aplicativo",
      "Productividad",
    ]);
  });

  it("compatibilidad: reportes.read (legado) muestra al menos Resumen general", async () => {
    setPermissions(["reportes.read"]);
    render(<Reportes />);

    const tabs = await screen.findAllByRole("tab");
    expect(tabs).toHaveLength(1);
    expect(tabs[0]).toHaveTextContent("Resumen general");
  });

  it("con un único slug temático, esa pestaña queda activa y carga sus datos", async () => {
    setPermissions(["reportes.uso.read"]);
    render(<Reportes />);

    const tab = await screen.findByRole("tab", { name: "Uso del aplicativo" });
    expect(tab).toHaveAttribute("aria-selected", "true");
    await waitFor(() => expect(mocks.fetchUsageMetrics).toHaveBeenCalledTimes(1));
    // Telemetría nueva sin datos → estado vacío específico.
    expect(await screen.findByText(/aún no hay datos de uso registrados/i)).toBeInTheDocument();
    // No se llamó nada de otras pestañas.
    expect(mocks.fetchAnalyticsOverview).not.toHaveBeenCalled();
  });

  it("sin ningún permiso de reportes muestra el estado vacío amable", async () => {
    setPermissions(["dashboard.read"]);
    render(<Reportes />);

    expect(await screen.findByTestId("reportes-sin-permisos")).toHaveTextContent(/no tienes permisos/i);
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    expect(mocks.fetchAnalyticsOverview).not.toHaveBeenCalled();
  });
});

// ── Pestaña Resumen ──────────────────────────────────────────────────────────
describe("Reportes — pestaña Resumen general", () => {
  it("carga y muestra los KPIs y el panel 'Ahora mismo'", async () => {
    render(<Reportes />);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    expect(screen.getByText("150")).toBeInTheDocument();
    expect(screen.getByText("Ahora mismo")).toBeInTheDocument();
    // Datos del live-overview (creados hoy = 14).
    expect(await screen.findByText("Creados hoy")).toBeInTheDocument();
    expect(screen.getByText("14")).toBeInTheDocument();
    expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(1);
    expect(mocks.fetchMonthlyTrend).toHaveBeenCalledTimes(1);
  });
});

// ── Filtros persistentes entre pestañas ──────────────────────────────────────
describe("Reportes — filtros globales persistentes", () => {
  it("al cambiar de pestaña se conservan los filtros activos", async () => {
    render(<Reportes />);
    await screen.findByText("Total trámites");

    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2026-01-05" } });
    await waitFor(() => {
      expect(mocks.fetchAnalyticsOverview.mock.calls.at(-1)?.[0]).toMatchObject({ from: "2026-01-05" });
    });

    fireEvent.click(screen.getByRole("tab", { name: "Operación / Trámites" }));

    await waitFor(() => {
      expect(mocks.fetchOtMetrics).toHaveBeenCalledTimes(1);
      expect(mocks.fetchOtMetrics.mock.calls.at(-1)?.[0]).toMatchObject({ from: "2026-01-05" });
      expect(mocks.fetchFunnel.mock.calls.at(-1)?.[0]).toMatchObject({ from: "2026-01-05" });
    });
  });
});

// ── Pestaña Operación: drill-down y atascados accionables ───────────────────
describe("Reportes — pestaña Operación / Trámites", () => {
  it("el embudo permite drill-down por estado (abre el panel de detalle)", async () => {
    render(<Reportes />);
    await screen.findByText("Total trámites");

    fireEvent.click(screen.getByRole("tab", { name: "Operación / Trámites" }));

    const stage = await screen.findByRole("button", { name: /ver trámites en estado preparado/i });
    fireEvent.click(stage);

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Detalle de trámites")).toBeInTheDocument();
    expect(await within(dialog).findByText("TRM-2026-000008")).toBeInTheDocument();
    await waitFor(() => {
      expect(mocks.fetchProcedureDetails.mock.calls.at(-1)?.[0]).toMatchObject({ status: "preparado" });
    });
  });

  it("la tabla de atascados enlaza cada fila a /tramites/{instanceId}", async () => {
    render(<Reportes />);
    await screen.findByText("Total trámites");

    fireEvent.click(screen.getByRole("tab", { name: "Operación / Trámites" }));

    const table = await screen.findByTestId("stuck-table");
    const link = within(table).getByRole("link", { name: /trm-2026-000001/i });
    expect(link).toHaveAttribute("href", "/tramites/inst-1");
  });
});

// ── SuperAdmin sin compañía elegida (§4: tenantId obligatorio) ───────────────
describe("Reportes — SuperAdmin sin compañía", () => {
  it("las pestañas nuevas muestran el aviso y NO llaman a la API", async () => {
    setPermissions([], true);
    render(<Reportes />);
    await screen.findByText("Total trámites");

    fireEvent.click(screen.getByRole("tab", { name: "Operación / Trámites" }));

    expect(await screen.findByTestId("aviso-selecciona-compania")).toHaveTextContent(/selecciona una compañía/i);
    expect(mocks.fetchOtMetrics).not.toHaveBeenCalled();
    expect(mocks.fetchFunnel).not.toHaveBeenCalled();
  });

  it("el panel 'Ahora mismo' del Resumen también pide compañía sin llamar al live-overview", async () => {
    setPermissions([], true);
    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(screen.getByTestId("aviso-selecciona-compania")).toBeInTheDocument();
    expect(mocks.fetchLiveOverview).not.toHaveBeenCalled();
  });
});

// ── Auto-refresh del panel "Ahora mismo" ─────────────────────────────────────
describe("LiveNowPanel — auto-refresh configurable", () => {
  it("refresca cada 45 s por defecto y se pausa/reanuda con el botón", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<LiveNowPanel tenantId="t-1" />);

    await waitFor(() => expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(1));
    await screen.findByText("Creados hoy");

    // Avanza el intervalo por defecto (45 s) → segundo fetch.
    await vi.advanceTimersByTimeAsync(45_000);
    expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(2);

    // Pausa: aunque pase el tiempo, no vuelve a consultar.
    fireEvent.click(screen.getByRole("button", { name: /pausar actualización/i }));
    await vi.advanceTimersByTimeAsync(120_000);
    expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(2);

    // Reanudar: retoma el polling.
    fireEvent.click(screen.getByRole("button", { name: /reanudar actualización/i }));
    await vi.advanceTimersByTimeAsync(45_000);
    expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(3);
  });

  it("se pausa automáticamente cuando la pestaña del navegador se oculta", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<LiveNowPanel tenantId="t-1" />);
    await waitFor(() => expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(1));

    // Documento oculto → el polling se detiene.
    act(() => {
      Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
      document.dispatchEvent(new Event("visibilitychange"));
    });
    await vi.advanceTimersByTimeAsync(120_000);
    expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(1);

    // Documento visible de nuevo → retoma.
    act(() => {
      Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
      document.dispatchEvent(new Event("visibilitychange"));
    });
    await vi.advanceTimersByTimeAsync(45_000);
    expect(mocks.fetchLiveOverview).toHaveBeenCalledTimes(2);
  });

  it("muestra el indicador 'actualizado hace Xs'", async () => {
    render(<LiveNowPanel tenantId="t-1" />);
    await screen.findByText("Creados hoy");
    expect(await screen.findByTestId("live-updated-ago")).toHaveTextContent(/actualizado hace \d+s/i);
  });
});

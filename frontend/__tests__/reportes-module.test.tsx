import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import type { AnalyticsOverviewResponse, CompanyListItem } from "@/lib/api/types";
import { ApiError } from "@/lib/api/types";
import type { LiveOverviewResponse } from "@/lib/api/analytics-v2";

// ── Mocks de la capa de datos y de permisos (sin red real) ──────────────────
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

import { Reportes } from "@/components/atom/modules/Reportes";

const FULL: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-06-01",
  to: "2026-06-26",
  categories: [
    { category: "matriculas", total: 120, byStatus: [{ status: "submitted", count: 80 }, { status: "approved", count: 40 }] },
    { category: "traspasos", total: 30, byStatus: [{ status: "submitted", count: 30 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const EMPTY: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-06-01",
  to: "2026-06-26",
  categories: [
    { category: "matriculas", total: 0, byStatus: [] },
    { category: "traspasos", total: 0, byStatus: [] },
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

const COMPANY: CompanyListItem = {
  id: "22222222-2222-2222-2222-222222222222",
  nit: "900123456",
  razonSocial: "Transportes Andinos S.A.S.",
  code: "AND",
  tenantType: "RENTING",
  isTransitOffice: false,
  estadoActivo: true,
  fechaCreacion: "2026-01-01T00:00:00Z",
  rowVersion: 1,
};

function permissionsState(overrides: Partial<ReturnType<typeof basePermissions>> = {}) {
  return { ...basePermissions(), ...overrides };
}

function basePermissions() {
  return {
    permissions: ["reportes.read"],
    isSuperAdmin: false,
    isAdminCompany: false,
    isOtAdmin: false,
    tenantId: null as string | null,
    userId: null as string | null,
    roleId: null as string | null,
    roleCode: null as string | null,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.usePermissions.mockReturnValue(permissionsState());
  mocks.fetchAnalyticsOverview.mockResolvedValue(FULL);
  mocks.fetchMonthlyTrend.mockResolvedValue({ items: [] });
  mocks.fetchLiveOverview.mockResolvedValue(LIVE);
  mocks.fetchCompaniesIndex.mockResolvedValue({ data: [COMPANY], totalCount: 1, page: 1, pageSize: 100 });
});

describe("Reportes — AC3 estados de UI (UiStateBoundary)", () => {
  it("Cargando: muestra el placeholder accesible antes de la primera respuesta", async () => {
    let resolveFn: (v: AnalyticsOverviewResponse) => void = () => {};
    mocks.fetchAnalyticsOverview.mockReturnValue(
      new Promise<AnalyticsOverviewResponse>((r) => {
        resolveFn = r;
      }),
    );

    render(<Reportes />);

    expect(screen.getAllByTestId("ui-loading").length).toBeGreaterThan(0);

    resolveFn(FULL);
    await screen.findByText("Total trámites");
  });

  it("Vacío: muestra mensaje cuando no hay trámites en el periodo", async () => {
    mocks.fetchAnalyticsOverview.mockResolvedValue(EMPTY);

    render(<Reportes />);

    expect(await screen.findByText(/no hay trámites para el rango/i)).toBeInTheDocument();
  });

  it("Error: muestra role=alert con reintento y vuelve a consultar al reintentar", async () => {
    mocks.fetchAnalyticsOverview.mockRejectedValueOnce(new ApiError(500, "boom"));

    render(<Reportes />);

    const alert = await screen.findByRole("alert");
    const retry = within(alert).getByRole("button", { name: /reintentar/i });

    mocks.fetchAnalyticsOverview.mockResolvedValue(FULL);
    fireEvent.click(retry);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(2);
  });

  it("Lleno: pinta el total real y los donuts por categoría", async () => {
    render(<Reportes />);

    expect(await screen.findByText("Total trámites")).toBeInTheDocument();
    // total = 120 + 30 + 0
    expect(screen.getByText("150")).toBeInTheDocument();
    // Títulos de los gráficos circulares (también aparecen como tarjeta KPI).
    expect(screen.getAllByText("Matrículas").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Traspasos").length).toBeGreaterThan(0);
    // Leyenda de estados (fuera del SVG, siempre presente).
    expect(screen.getByText("Aprobado")).toBeInTheDocument();
    // Categoría sin datos muestra su nota vacía.
    expect(screen.getByTestId("donut-empty-otros")).toBeInTheDocument();
  });

  it("Vehicular: pinta la categoría como donut de primer nivel (HU #10433)", async () => {
    const withVehicular: AnalyticsOverviewResponse = {
      ...FULL,
      categories: [
        { category: "matriculas", total: 10, byStatus: [{ status: "submitted", count: 10 }] },
        { category: "traspasos", total: 0, byStatus: [] },
        { category: "vehicular", total: 45, byStatus: [{ status: "submitted", count: 45 }] },
        { category: "otros", total: 0, byStatus: [] },
      ],
    };
    mocks.fetchAnalyticsOverview.mockResolvedValue(withVehicular);

    render(<Reportes />);

    await screen.findByText("Total trámites");
    // total = 10 + 0 + 45 + 0 (vehicular ya NO se pierde en "otros")
    expect(screen.getByText("55")).toBeInTheDocument();
    // La categoría Vehicular aparece como tarjeta/donut con su etiqueta de marca.
    expect(screen.getAllByText("Vehicular").length).toBeGreaterThan(0);
  });
});

describe("Reportes — AC1 acceso por rol", () => {
  it("Tenant Admin: NO ve el selector de compañía", async () => {
    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(screen.queryByLabelText("Compañía")).not.toBeInTheDocument();
    expect(mocks.fetchCompaniesIndex).not.toHaveBeenCalled();
  });

  it("SuperAdmin: ve el selector con sus compañías y filtra por tenantId", async () => {
    mocks.usePermissions.mockReturnValue(permissionsState({ isSuperAdmin: true }));

    render(<Reportes />);

    const selector = await screen.findByLabelText("Compañía");
    expect(await screen.findByRole("option", { name: "Transportes Andinos S.A.S." })).toBeInTheDocument();

    fireEvent.change(selector, { target: { value: COMPANY.id } });

    await waitFor(() => {
      const lastCall = mocks.fetchAnalyticsOverview.mock.calls.at(-1);
      expect(lastCall?.[0]).toMatchObject({ tenantId: COMPANY.id });
    });
  });
});

describe("Reportes — AC2 filtro de fechas en tiempo real", () => {
  it("al cambiar el rango vuelve a consultar la API con las nuevas fechas", async () => {
    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2020-01-01" } });

    await waitFor(() => {
      const lastCall = mocks.fetchAnalyticsOverview.mock.calls.at(-1);
      expect(lastCall?.[0]).toMatchObject({ from: "2020-01-01" });
    });
  });

  it("rango inválido (inicio posterior al fin): muestra error sin llamar a la API de nuevo", async () => {
    render(<Reportes />);

    await screen.findByText("Total trámites");
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Desde"), { target: { value: "2099-12-31" } });

    expect(await screen.findByText(/la fecha inicial no puede ser posterior/i)).toBeInTheDocument();
    expect(mocks.fetchAnalyticsOverview).toHaveBeenCalledTimes(1);
  });
});

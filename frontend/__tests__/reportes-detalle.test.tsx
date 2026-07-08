import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

import type {
  AnalyticsOverviewResponse,
  ProcedureDetailsPage,
  TopProducer,
  TopProducersResponse,
} from "@/lib/api/types";
import type { LiveOverviewResponse } from "@/lib/api/analytics-v2";

// ── Mocks de la capa de datos y de permisos ─────────────────────────────────
const mocks = vi.hoisted(() => ({
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchTopProducers: vi.fn(),
  fetchProcedureDetails: vi.fn(),
  exportAnalyticsExcel: vi.fn(),
  exportExecutivePdf: vi.fn(),
  fetchLiveOverview: vi.fn(),
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
  return { ...actual, fetchLiveOverview: mocks.fetchLiveOverview };
});
vi.mock("@/lib/api/admin-companies", () => ({ fetchCompaniesIndex: mocks.fetchCompaniesIndex }));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { Reportes } from "@/components/atom/modules/Reportes";
import { ProductivityCards } from "@/components/atom/modules/_reportes/ProductivityCards";

const OVERVIEW: AnalyticsOverviewResponse = {
  tenantId: "11111111-1111-1111-1111-111111111111",
  from: "2026-06-01",
  to: "2026-06-26",
  categories: [
    { category: "matriculas", total: 120, byStatus: [{ status: "submitted", count: 80 }, { status: "approved", count: 40 }] },
    { category: "traspasos", total: 30, byStatus: [{ status: "submitted", count: 30 }] },
    { category: "otros", total: 0, byStatus: [] },
  ],
};

const LIVE: LiveOverviewResponse = {
  generatedAt: "2026-07-07T14:03:22Z",
  today: { creados: 2, byStatus: [], entregados: 1, aprobados: 0, rechazados: 0 },
  stuckCount: 0,
  pendingIdentityValidations: 0,
  integrationsLastHour: { calls: 0, errors: 0, avgDurationMs: 0 },
  lastActivityAt: null,
};

const PRODUCERS: TopProducer[] = [
  { userId: "u1", displayName: "Ana Pérez", submittedCount: 40, approvedCount: 12, rejectedCount: 3 },
  { userId: "u2", displayName: "Luis Gómez", submittedCount: 25, approvedCount: 8, rejectedCount: 1 },
];

const PRODUCERS_RESPONSE: TopProducersResponse = { items: PRODUCERS };

const DETAIL_PAGE: ProcedureDetailsPage = {
  items: [
    {
      id: "p1",
      referenceNumber: "TRM-2026-000008",
      procedureTypeName: "Matrícula nueva de vehículo",
      category: "matriculas",
      status: "submitted",
      createdByDisplayName: "Ana Pérez",
      submittedAt: "2026-06-10T12:00:00Z",
      completedAt: null,
    },
  ],
  totalCount: 80,
  page: 1,
  pageSize: 10,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.usePermissions.mockReturnValue({
    permissions: ["reportes.read"],
    isSuperAdmin: false,
    isAdminCompany: false,
    isOtAdmin: false,
    tenantId: null,
    userId: null,
    roleId: null,
    roleCode: null,
  });
  mocks.fetchAnalyticsOverview.mockResolvedValue(OVERVIEW);
  mocks.fetchMonthlyTrend.mockResolvedValue({ items: [] });
  mocks.fetchLiveOverview.mockResolvedValue(LIVE);
  mocks.fetchTopProducers.mockResolvedValue(PRODUCERS_RESPONSE);
  mocks.fetchProcedureDetails.mockResolvedValue(DETAIL_PAGE);
  mocks.fetchCompaniesIndex.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 100 });
});

describe("Reportes — AC1 tabla lateral al clic en un segmento", () => {
  it("al hacer clic en un estado del donut abre el panel con la tabla paginada", async () => {
    render(<Reportes />);

    // Esperar render del dashboard y la leyenda clicable del donut de Matrículas.
    const legend = await screen.findByRole("button", { name: /ver radicado de matrículas/i });
    fireEvent.click(legend);

    // Panel lateral (dialog) con el detalle filtrado.
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Detalle de trámites")).toBeInTheDocument();
    expect(await within(dialog).findByText("TRM-2026-000008")).toBeInTheDocument();

    // Se consultó el detalle con la categoría y el estado del segmento.
    await waitFor(() => {
      const lastCall = mocks.fetchProcedureDetails.mock.calls.at(-1);
      expect(lastCall?.[0]).toMatchObject({ category: "matriculas", status: "submitted", page: 1 });
    });
  });

  it("permite cerrar el panel lateral", async () => {
    render(<Reportes />);

    const legend = await screen.findByRole("button", { name: /ver radicado de matrículas/i });
    fireEvent.click(legend);

    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: /cerrar detalle/i }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});

describe("ProductivityCards — AC2 multiselect y AC3 sin trámites", () => {
  it("AC2: agrega todos por defecto y filtra al seleccionar usuarios", async () => {
    render(<ProductivityCards producers={PRODUCERS} status="ready" />);

    // Por defecto agrega a todos: enviados = 40 + 25 = 65.
    expect(screen.getByText("Enviados").parentElement).toHaveTextContent("65");

    // Seleccionar solo a Ana → enviados = 40.
    fireEvent.click(screen.getByRole("button", { name: /ana pérez/i }));
    await waitFor(() => expect(screen.getByText("Enviados").parentElement).toHaveTextContent("40"));
  });

  it("AC3: un usuario sin actividad muestra 'Sin trámites en el periodo seleccionado'", async () => {
    const idle: TopProducer[] = [
      { userId: "z", displayName: "Sin Actividad", submittedCount: 0, approvedCount: 0, rejectedCount: 0 },
    ];
    render(<ProductivityCards producers={idle} status="ready" />);

    expect(screen.getByTestId("productividad-sin-tramites")).toHaveTextContent(/sin trámites en el periodo seleccionado/i);
  });

  it("muestra estado vacío cuando no hay productores en el periodo", () => {
    render(<ProductivityCards producers={[]} status="empty" />);
    expect(screen.getByTestId("ui-empty")).toHaveTextContent(/no hay productividad/i);
  });
});

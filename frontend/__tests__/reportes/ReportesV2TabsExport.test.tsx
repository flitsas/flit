// HU #11114 — Reportes V2: 8 tabs canónicos, teclado WCAG, ExportController.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  countPendingExports,
  ExportController,
} from "@/components/atom/modules/_reportes/ExportController";
import { ReportesTabBar } from "@/components/atom/modules/_reportes/ReportesTabBar";
import {
  REPORTES_V2_TAB_ORDER,
  Reportes,
} from "@/components/atom/modules/Reportes";

const mocks = vi.hoisted(() => ({
  usePermissions: vi.fn(),
  listExports: vi.fn(),
  requestExport: vi.fn(),
  getExportDownloadUrl: vi.fn(),
  watchExportJob: vi.fn(),
  getDashboardPreferences: vi.fn(),
  fetchAnalyticsOverview: vi.fn(),
  fetchMonthlyTrend: vi.fn(),
  fetchTopProducers: vi.fn(),
  fetchLiveOverview: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
  fetchReportingProcedures: vi.fn(),
  fetchConsolidado: vi.fn(),
  fetchProductivity: vi.fn(),
  fetchSla: vi.fn(),
  fetchProcedureAudit: vi.fn(),
  fetchReportSchedules: vi.fn(),
  fetchAlertRules: vi.fn(),
}));

vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));
vi.mock("@/lib/api/reporting-v2", () => ({
  listExports: (...a: unknown[]) => mocks.listExports(...a),
  requestExport: (...a: unknown[]) => mocks.requestExport(...a),
  getExportDownloadUrl: (...a: unknown[]) => mocks.getExportDownloadUrl(...a),
  getDashboardPreferences: (...a: unknown[]) => mocks.getDashboardPreferences(...a),
  fetchReportingProcedures: (...a: unknown[]) => mocks.fetchReportingProcedures(...a),
  fetchConsolidado: (...a: unknown[]) => mocks.fetchConsolidado(...a),
  fetchProductivity: (...a: unknown[]) => mocks.fetchProductivity(...a),
  fetchSla: (...a: unknown[]) => mocks.fetchSla(...a),
  fetchProcedureAudit: (...a: unknown[]) => mocks.fetchProcedureAudit(...a),
}));
vi.mock("@/lib/signalr/export-jobs-client", () => ({
  watchExportJob: (...a: unknown[]) => mocks.watchExportJob(...a),
}));
vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: (...a: unknown[]) => mocks.fetchAnalyticsOverview(...a),
  fetchMonthlyTrend: (...a: unknown[]) => mocks.fetchMonthlyTrend(...a),
  fetchTopProducers: (...a: unknown[]) => mocks.fetchTopProducers(...a),
  fetchProcedureDetails: vi.fn().mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 10 }),
  exportAnalyticsExcel: vi.fn(),
  exportExecutivePdf: vi.fn(),
}));
vi.mock("@/lib/api/analytics-v2", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/analytics-v2")>();
  return {
    ...actual,
    fetchLiveOverview: (...a: unknown[]) => mocks.fetchLiveOverview(...a),
    fetchOtMetrics: vi.fn(),
    fetchFunnel: vi.fn(),
    fetchUsageMetrics: vi.fn(),
  };
});
vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...a: unknown[]) => mocks.fetchCompaniesIndex(...a),
}));
vi.mock("@/lib/api/analytics-scheduling", () => ({
  fetchReportSchedules: (...a: unknown[]) => mocks.fetchReportSchedules(...a),
  fetchAlertRules: (...a: unknown[]) => mocks.fetchAlertRules(...a),
  createReportSchedule: vi.fn(),
  updateReportSchedule: vi.fn(),
  deleteReportSchedule: vi.fn(),
  createAlertRule: vi.fn(),
  updateAlertRule: vi.fn(),
  deleteAlertRule: vi.fn(),
  fetchAlertEvents: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
}));

const V2_SLUGS = [
  "reporting.read",
  "reporting.consolidado",
  "reporting.productivity",
  "reporting.audit",
  "reporting.schedules.read",
  "reporting.alerts.read",
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
  setPermissions(V2_SLUGS);
  mocks.listExports.mockResolvedValue({ items: [] });
  mocks.watchExportJob.mockResolvedValue(() => {});
  mocks.getDashboardPreferences.mockResolvedValue({ configJson: null });
  mocks.fetchAnalyticsOverview.mockResolvedValue({
    tenantId: "t1",
    from: "2026-07-01",
    to: "2026-07-07",
    categories: [],
  });
  mocks.fetchMonthlyTrend.mockResolvedValue({ items: [] });
  mocks.fetchTopProducers.mockResolvedValue({ items: [] });
  mocks.fetchLiveOverview.mockResolvedValue({
    generatedAt: "2026-07-07T14:03:22Z",
    today: { creados: 0, byStatus: [], entregados: 0, aprobados: 0, rechazados: 0 },
    stuckCount: 0,
    pendingIdentityValidations: 0,
    integrationsLastHour: { calls: 0, errors: 0, avgDurationMs: 0 },
    lastActivityAt: null,
  });
  mocks.fetchCompaniesIndex.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 100 });
  mocks.fetchReportingProcedures.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  mocks.fetchConsolidado.mockResolvedValue({ groupBy: "tipo", items: [] });
  mocks.fetchProductivity.mockResolvedValue({ items: [] });
  mocks.fetchSla.mockResolvedValue({ slaConfigured: false, items: [] });
  mocks.fetchProcedureAudit.mockResolvedValue({ items: [], totalCount: 0 });
  mocks.fetchReportSchedules.mockResolvedValue({ items: [] });
  mocks.fetchAlertRules.mockResolvedValue({ items: [] });
  window.history.replaceState(null, "", "/");
});

describe("REPORTES_V2_TAB_ORDER (AC1)", () => {
  it("define las 8 pestañas en orden canónico", () => {
    expect([...REPORTES_V2_TAB_ORDER]).toEqual([
      "resumen",
      "tramites",
      "consolidado",
      "productividad",
      "tiempos-sla",
      "auditoria",
      "programados",
      "alertas",
    ]);
  });
});

describe("Reportes — 8 tabs V2 (AC1)", () => {
  it("muestra las 8 pestañas en orden con permisos V2", async () => {
    render(<Reportes />);
    const tabs = await screen.findAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual([
      "Resumen",
      "Trámites",
      "Consolidado",
      "Productividad",
      "Tiempos / SLA",
      "Auditoría",
      "Programados",
      "Alertas",
    ]);
    expect(tabs[0]).toHaveAttribute("aria-selected", "true");
  });

  it("default activo es resumen", async () => {
    render(<Reportes />);
    const resumen = await screen.findByTestId("reportes-tab-resumen");
    expect(resumen).toHaveAttribute("aria-selected", "true");
  });

  it("abre tabs programados y alertas", async () => {
    render(<Reportes />);
    fireEvent.click(await screen.findByRole("tab", { name: "Programados" }));
    expect(await screen.findByTestId("tab-programados")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "Alertas" }));
    expect(await screen.findByTestId("tab-alertas")).toBeInTheDocument();
  });
});

describe("ReportesTabBar — teclado (AC1)", () => {
  it("ArrowRight / ArrowLeft cambian pestaña activa", async () => {
    const onChange = vi.fn();
    const tabs = [
      { id: "resumen", label: "Resumen" },
      { id: "tramites", label: "Trámites" },
      { id: "consolidado", label: "Consolidado" },
    ];
    render(
      <ReportesTabBar tabs={tabs} activeId="resumen" onChange={onChange} ariaLabel="tabs" />,
    );
    const first = screen.getByRole("tab", { name: "Resumen" });
    first.focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(onChange).toHaveBeenCalledWith("tramites");
  });
});

describe("countPendingExports (AC2/AC6)", () => {
  it("cuenta solo pending/processing; failed no entra al badge", () => {
    expect(
      countPendingExports([
        { status: "pending" },
        { status: "processing" },
        { status: "failed" },
        { status: "completed" },
      ]),
    ).toBe(2);
  });
});

describe("ExportController (AC2–AC6)", () => {
  it("badge con aria-label de exportaciones en progreso", async () => {
    mocks.listExports.mockResolvedValue({
      items: [
        {
          id: "j1",
          status: "processing",
          progressPct: 40,
          format: "csv",
          reportType: "procedures",
        },
        {
          id: "j2",
          status: "failed",
          progressPct: 0,
          format: "excel",
          reportType: "procedures",
          errorMessage: "boom",
        },
      ],
    });

    render(
      <ExportController reportType="procedures" from="2026-01-01" to="2026-01-31" />,
    );

    const badge = await screen.findByTestId("export-pending-badge");
    await waitFor(() => expect(badge).toHaveTextContent("1"));
    expect(badge).toHaveAttribute("aria-label", "1 exportaciones en progreso");
  });

  it("muestra empty state canónico", async () => {
    render(
      <ExportController reportType="procedures" from="2026-01-01" to="2026-01-31" />,
    );
    expect(await screen.findByTestId("export-empty")).toHaveTextContent(
      "Sin exportaciones recientes",
    );
  });

  it("barra de progreso con aria-live y porcentaje", async () => {
    mocks.listExports.mockResolvedValue({
      items: [
        {
          id: "prog-1",
          status: "processing",
          progressPct: 55,
          format: "csv",
          reportType: "procedures",
        },
      ],
    });

    render(
      <ExportController reportType="procedures" from="2026-01-01" to="2026-01-31" />,
    );

    const bar = await screen.findByTestId("export-progress-prog-1");
    expect(bar).toHaveAttribute("aria-valuenow", "55");
    expect(screen.getByText("55%")).toBeInTheDocument();
    expect(screen.getByRole("list")).toHaveAttribute("aria-live", "polite");
  });

  it("toast ExportCompleted con Descargar", async () => {
    let handlers: {
      onCompleted?: (e: { jobId: string; status: string; progressPct: number }) => void;
    } = {};
    mocks.watchExportJob.mockImplementation(async (_id, h) => {
      handlers = h;
      return () => {};
    });
    mocks.listExports.mockResolvedValue({ items: [] });
    mocks.requestExport.mockResolvedValue({
      id: "done-1",
      status: "pending",
      progressPct: 0,
      format: "csv",
      reportType: "procedures",
    });
    mocks.getExportDownloadUrl.mockResolvedValue({
      downloadUrl: "https://example.com/file.csv",
    });

    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);

    render(
      <ExportController reportType="procedures" from="2026-01-01" to="2026-01-31" />,
    );
    await screen.findByTestId("export-empty");

    fireEvent.click(screen.getByRole("button", { name: "CSV" }));
    await waitFor(() => expect(mocks.requestExport).toHaveBeenCalled());
    await waitFor(() => expect(mocks.watchExportJob).toHaveBeenCalled());

    await act(async () => {
      handlers.onCompleted?.({ jobId: "done-1", status: "completed", progressPct: 100 });
    });

    expect(await screen.findByTestId("export-toast-success")).toHaveTextContent(
      "Exportación lista",
    );
    const toast = screen.getByTestId("export-toast-success");
    fireEvent.click(within(toast).getByRole("button", { name: "Descargar" }));
    await waitFor(() =>
      expect(mocks.getExportDownloadUrl).toHaveBeenCalledWith("done-1"),
    );
    expect(openSpy).toHaveBeenCalledWith(
      "https://example.com/file.csv",
      "_blank",
      "noopener,noreferrer",
    );
    openSpy.mockRestore();
  });

  it("toast ExportFailed y failed no suma al badge", async () => {
    let handlers: {
      onFailed?: (e: { jobId: string; status: string; progressPct: number }) => void;
    } = {};
    mocks.watchExportJob.mockImplementation(async (_id, h) => {
      handlers = h;
      return () => {};
    });
    mocks.listExports.mockResolvedValue({
      items: [
        {
          id: "fail-1",
          status: "processing",
          progressPct: 10,
          format: "pdf",
          reportType: "procedures",
          errorMessage: "Timeout al generar",
        },
      ],
    });

    render(
      <ExportController reportType="procedures" from="2026-01-01" to="2026-01-31" />,
    );

    const badge = await screen.findByTestId("export-pending-badge");
    await waitFor(() => expect(badge).toHaveTextContent("1"));

    await act(async () => {
      handlers.onFailed?.({ jobId: "fail-1", status: "failed", progressPct: 10 });
    });

    expect(await screen.findByTestId("export-toast-error")).toHaveTextContent(
      "Timeout al generar",
    );
    await waitFor(() => expect(badge).toHaveTextContent("0"));
    expect(badge).toHaveAttribute("aria-label", "0 exportaciones en progreso");
  });
});

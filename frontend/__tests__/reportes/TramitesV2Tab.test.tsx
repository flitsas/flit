// HU #11115 — Tab Trámites V2: tabla, filtros, empty/error, rango 12m, export.
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  buildActiveFilterChips,
  TramitesV2Tab,
} from "@/components/atom/modules/_reportes/tabs/TramitesV2Tab";
import {
  ReportFilterProvider,
  defaultReportingV2Filters,
} from "@/components/atom/modules/_reportes/ReportFilterContext";
import { isWithinMaxMonths } from "@/components/atom/modules/_reportes/range";
import { ApiError } from "@/lib/api/types";
import { FLIT_EXPORT_JOB_CREATED } from "@/components/atom/modules/_reportes/export-events";

const mocks = vi.hoisted(() => ({
  fetchReportingProcedures: vi.fn(),
  requestExport: vi.fn(),
  usePermissions: vi.fn(),
}));

vi.mock("@/lib/api/reporting-v2", () => ({
  fetchReportingProcedures: (...a: unknown[]) => mocks.fetchReportingProcedures(...a),
  requestExport: (...a: unknown[]) => mocks.requestExport(...a),
}));
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => mocks.usePermissions(),
}));

function renderTab(initialSearch = "") {
  return render(
    <ReportFilterProvider initialSearch={initialSearch}>
      <TramitesV2Tab />
    </ReportFilterProvider>,
  );
}

const PAGE = {
  items: [
    {
      id: "1",
      referenceNumber: "TRM-1",
      procedureType: "traslado",
      status: "en_proceso",
      plate: "ABC123",
      transitOfficeName: "OT1",
      createdAt: "2026-07-01T10:00:00Z",
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 50,
  kpis: { total: 1, approved: 0, rejected: 0, inProgress: 1, avgElapsedHours: 2 },
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.usePermissions.mockReturnValue({
    permissions: ["reporting.read", "reporting.export"],
    isSuperAdmin: false,
  });
  mocks.fetchReportingProcedures.mockResolvedValue(PAGE);
  mocks.requestExport.mockResolvedValue({
    id: "job-1",
    status: "pending",
    progressPct: 0,
    format: "excel",
    reportType: "procedures",
  });
  window.history.replaceState(null, "", "/?m=reportes&reportesTab=tramites");
});

afterEach(() => {
  vi.useRealTimers();
});

describe("isWithinMaxMonths (AC6)", () => {
  it("bloquea rangos de 24 meses", () => {
    expect(isWithinMaxMonths({ from: "2024-01-01", to: "2026-01-01" }, 12)).toBe(false);
  });
  it("permite exactamente 12 meses", () => {
    expect(isWithinMaxMonths({ from: "2025-01-01", to: "2026-01-01" }, 12)).toBe(true);
  });
});

describe("buildActiveFilterChips (AC2)", () => {
  it("incluye status y procedureType", () => {
    const defaults = defaultReportingV2Filters(new Date("2026-07-30"));
    const chips = buildActiveFilterChips(
      { ...defaults, status: "en_proceso", procedureType: "traslado" },
      defaults,
    );
    expect(chips.map((c) => c.key)).toEqual(expect.arrayContaining(["status", "procedureType"]));
  });
});

describe("TramitesV2Tab AC1 — carga default 30d + skeleton", () => {
  it("llama GET procedures con from/to del contexto (default ~30d) y pageSize 50", async () => {
    renderTab();
    expect(screen.getByTestId("tramites-skeleton")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchReportingProcedures).toHaveBeenCalled());
    const args = mocks.fetchReportingProcedures.mock.calls[0]![0] as {
      from: string;
      to: string;
      pageSize: number;
    };
    expect(args.pageSize).toBe(50);
    expect(args.from < args.to || args.from === args.to).toBe(true);
    // ~30 días: diferencia <= 31 días
    const fromMs = new Date(`${args.from}T00:00:00`).getTime();
    const toMs = new Date(`${args.to}T00:00:00`).getTime();
    expect((toMs - fromMs) / 86400000).toBeLessThanOrEqual(31);
    expect(await screen.findByTestId("tramites-v2-table")).toBeInTheDocument();
  });
});

describe("TramitesV2Tab AC2 — filtros URL + chips", () => {
  it("al aplicar status y procedureType recarga y muestra chips", async () => {
    const user = userEvent.setup();
    renderTab();
    await screen.findByTestId("tramites-v2-table");

    await user.selectOptions(screen.getByTestId("tramites-filter-status"), "en_proceso");
    await user.selectOptions(screen.getByTestId("tramites-filter-procedure-type"), "traslado");

    await waitFor(() => {
      const last = mocks.fetchReportingProcedures.mock.calls.at(-1)?.[0] as {
        status: string;
        procedureType: string;
      };
      expect(last.status).toBe("en_proceso");
      expect(last.procedureType).toBe("traslado");
    });

    const chips = await screen.findByTestId("tramites-filter-chips");
    expect(within(chips).getByText(/Estado: en_proceso/)).toBeInTheDocument();
    expect(within(chips).getByText(/Tipo: traslado/)).toBeInTheDocument();
  });
});

describe("TramitesV2Tab AC3 — export Excel", () => {
  it("POST exports con filtros del contexto y dispara evento al ExportController", async () => {
    const user = userEvent.setup();
    const listener = vi.fn();
    window.addEventListener(FLIT_EXPORT_JOB_CREATED, listener);

    renderTab("?status=en_proceso");
    await screen.findByTestId("tramites-v2-table");
    await user.click(screen.getByTestId("tramites-export-excel"));

    await waitFor(() => expect(mocks.requestExport).toHaveBeenCalled());
    expect(mocks.requestExport.mock.calls[0]![0]).toMatchObject({
      reportType: "procedures",
      format: "excel",
      filters: expect.objectContaining({ status: "en_proceso" }),
    });
    expect(listener).toHaveBeenCalled();
    window.removeEventListener(FLIT_EXPORT_JOB_CREATED, listener);
  });
});

describe("TramitesV2Tab AC4 — vacío", () => {
  it("muestra mensaje canónico con icono", async () => {
    mocks.fetchReportingProcedures.mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
      kpis: { total: 0, approved: 0, rejected: 0, inProgress: 0 },
    });
    renderTab();
    const empty = await screen.findByTestId("tramites-empty");
    expect(empty).toHaveTextContent("Sin datos para el período seleccionado");
  });
});

describe("TramitesV2Tab AC5 — error HTTP + Reintentar", () => {
  it("muestra banner con código y reintenta", async () => {
    const user = userEvent.setup();
    mocks.fetchReportingProcedures
      .mockRejectedValueOnce(new ApiError(500, "Error 500 al llamar /api/v1/reporting/procedures"))
      .mockResolvedValueOnce(PAGE);

    renderTab();
    const banner = await screen.findByTestId("tramites-error-banner");
    expect(banner).toHaveTextContent(/HTTP 500/);
    await user.click(screen.getByTestId("tramites-retry"));
    expect(await screen.findByTestId("tramites-v2-table")).toBeInTheDocument();
  });
});

describe("TramitesV2Tab AC6 — rango > 12 meses", () => {
  it("muestra mensaje inline y no llama al backend", async () => {
    renderTab("?from=2024-01-01&to=2026-01-01");
    expect(await screen.findByTestId("tramites-range-error")).toHaveTextContent(
      "Rango máximo 12 meses",
    );
    await waitFor(() => {
      expect(mocks.fetchReportingProcedures).not.toHaveBeenCalled();
    });
  });
});

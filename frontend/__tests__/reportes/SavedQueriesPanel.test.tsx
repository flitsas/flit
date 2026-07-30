import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SavedQueriesPanel } from "../../components/atom/modules/_reportes/SavedQueriesPanel";
import { ReportFilterProvider, useReportFilters } from "../../components/atom/modules/_reportes/ReportFilterContext";
import { MAX_SAVED_QUERIES } from "../../components/atom/modules/_reportes/dashboardPreferences";
import {
  moveKpi,
  parseDashboardPreferences,
  defaultDashboardPreferences,
} from "../../components/atom/modules/_reportes/dashboardPreferences";

const listMock = vi.fn();
const createMock = vi.fn();

vi.mock("@/lib/api/reporting-v2", () => ({
  listSavedQueries: (...args: unknown[]) => listMock(...args),
  createSavedQuery: (...args: unknown[]) => createMock(...args),
}));

function FiltersProbe() {
  const { filters } = useReportFilters();
  return (
    <div>
      <span data-testid="ctx-status">{filters.status}</span>
      <span data-testid="ctx-ptype">{filters.procedureType}</span>
    </div>
  );
}

describe("dashboardPreferences helpers", () => {
  it("parseDashboardPreferences respeta visible=false de tramitesRechazados (AC1)", () => {
    const cfg = parseDashboardPreferences({
      kpis: [{ id: "tramitesRechazados", visible: false }],
    });
    expect(cfg.kpis.find((k) => k.id === "tramitesRechazados")?.visible).toBe(false);
    expect(cfg.kpis.length).toBe(defaultDashboardPreferences().kpis.length);
  });

  it("moveKpi reordena con teclado (AC4)", () => {
    const base = defaultDashboardPreferences().kpis;
    const moved = moveKpi(base, 0, 1);
    expect(moved[0]?.id).toBe(base[1]?.id);
    expect(moved[1]?.id).toBe(base[0]?.id);
  });
});

describe("SavedQueriesPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    listMock.mockResolvedValue({
      items: [
        {
          id: "q1",
          name: "En proceso traslado",
          filtersJson: { status: "en_proceso", procedureType: "traslado" },
          isShared: false,
          createdAt: "2026-07-30T00:00:00Z",
        },
      ],
    });
  });

  it("aplica saved query al ReportFilterContext (AC2)", async () => {
    const user = userEvent.setup();
    render(
      <ReportFilterProvider initialSearch="">
        <FiltersProbe />
        <SavedQueriesPanel open onClose={() => undefined} />
      </ReportFilterProvider>,
    );

    await waitFor(() => expect(screen.getByText("En proceso traslado")).toBeInTheDocument());
    await user.click(screen.getByTestId("saved-query-apply-q1"));
    expect(screen.getByTestId("ctx-status")).toHaveTextContent("en_proceso");
    expect(screen.getByTestId("ctx-ptype")).toHaveTextContent("traslado");
  });

  it("bloquea crear la consulta 21 sin llamar al backend (AC5)", async () => {
    listMock.mockResolvedValue({
      items: Array.from({ length: MAX_SAVED_QUERIES }, (_, i) => ({
        id: `q${i}`,
        name: `Q${i}`,
        filtersJson: {},
        isShared: false,
        createdAt: "2026-07-30T00:00:00Z",
      })),
    });
    const user = userEvent.setup();
    render(
      <ReportFilterProvider initialSearch="">
        <SavedQueriesPanel open onClose={() => undefined} />
      </ReportFilterProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("saved-query-save")).toBeInTheDocument());
    await user.type(screen.getByTestId("saved-query-name"), "Q-OT1");
    await user.click(screen.getByTestId("saved-query-save"));
    expect(screen.getByTestId("saved-query-limit")).toHaveTextContent(
      "Límite de consultas guardadas alcanzado",
    );
    expect(createMock).not.toHaveBeenCalled();
  });

  it("guarda consulta actual vía POST (AC3)", async () => {
    createMock.mockResolvedValue({
      id: "new",
      name: "Q-OT1",
      filtersJson: { status: "en_proceso" },
      isShared: false,
      createdAt: "2026-07-30T00:00:00Z",
    });
    const user = userEvent.setup();
    render(
      <ReportFilterProvider initialSearch="?status=en_proceso">
        <SavedQueriesPanel open onClose={() => undefined} />
      </ReportFilterProvider>,
    );
    await waitFor(() => expect(screen.getByTestId("saved-query-save")).toBeInTheDocument());
    await user.type(screen.getByTestId("saved-query-name"), "Q-OT1");
    await user.click(screen.getByTestId("saved-query-save"));
    await waitFor(() => expect(createMock).toHaveBeenCalled());
    expect(createMock.mock.calls[0]?.[0]?.name).toBe("Q-OT1");
  });
});

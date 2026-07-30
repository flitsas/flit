import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  ReportFilterProvider,
  parseReportingFiltersFromSearch,
  useReportFilters,
} from "../../components/atom/modules/_reportes/ReportFilterContext";

function Probe() {
  const { filters, patchFilters } = useReportFilters();
  return (
    <div>
      <span data-testid="status">{filters.status}</span>
      <span data-testid="from">{filters.from}</span>
      <button type="button" onClick={() => patchFilters({ status: "en_proceso", from: "2026-01-01" })}>
        aplicar
      </button>
    </div>
  );
}

describe("ReportFilterContext", () => {
  const replaceState = vi.fn();

  beforeEach(() => {
    replaceState.mockClear();
    vi.spyOn(window.history, "replaceState").mockImplementation(replaceState);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("parsea filtros desde URL (AC1 restore)", () => {
    const filters = parseReportingFiltersFromSearch(
      "?status=en_proceso&from=2026-01-01&to=2026-01-31&page=2",
    );
    expect(filters.status).toBe("en_proceso");
    expect(filters.from).toBe("2026-01-01");
    expect(filters.to).toBe("2026-01-31");
    expect(filters.page).toBe(2);
  });

  it("persiste filtros en URL al aplicar cambios (AC1)", async () => {
    const user = userEvent.setup();
    render(
      <ReportFilterProvider initialSearch="">
        <Probe />
      </ReportFilterProvider>,
    );

    await user.click(screen.getByRole("button", { name: "aplicar" }));
    expect(screen.getByTestId("status")).toHaveTextContent("en_proceso");
    expect(screen.getByTestId("from")).toHaveTextContent("2026-01-01");
    expect(replaceState).toHaveBeenCalled();
    const urlArg = String(replaceState.mock.calls.at(-1)?.[2] ?? "");
    expect(urlArg).toContain("status=en_proceso");
    expect(urlArg).toContain("from=2026-01-01");
  });
});

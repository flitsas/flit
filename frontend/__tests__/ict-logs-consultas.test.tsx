// IctLogs (HU #11610): pestaña "Consultas" nueva + botón "Programación" que reutiliza
// SchedulingPanel con los 4 reportType de ICT. "ict_jobs" agrega ict.job_runs, una tabla
// platform-wide sin tenant_id (ver ReportSchedulesEndpoints.IctJobsSuperAdminOnly en el backend),
// así que el selector de tipo de informe solo debe ofrecerlo a SuperAdmin.
//
// La pestaña "Alertas ICT" no se toca en esta HU: no hay pruebas suyas aquí a propósito.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  isSuperAdmin: vi.fn(() => false),
  fetchIctQueryFields: vi.fn(),
  fetchIctSavedQueries: vi.fn(),
  runIctQuery: vi.fn(),
}));

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return {
    ...actual,
    decodeJwtPayload: () => ({ tenant_id: "tenant-super" }),
    isSuperAdmin: mocks.isSuperAdmin,
  };
});

vi.mock("@/lib/api/ict-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ict-queries")>();
  return {
    ...actual,
    fetchIctQueryFields: mocks.fetchIctQueryFields,
    fetchIctSavedQueries: mocks.fetchIctSavedQueries,
    runIctQuery: mocks.runIctQuery,
  };
});

// El resto de llamadas de red del módulo (logs, alertas, report-schedules, alert-rules) se
// resuelven vacías: no son el objeto de esta prueba.
vi.mock("@/lib/api/ict-client", () => ({
  fetchIctLogs: vi.fn().mockResolvedValue({ items: [], total: 0 }),
  fetchIctAlerts: vi.fn().mockResolvedValue({
    stuckInValidation: 0,
    noveltyRatePct: 0,
    webhookDeliveryFailures: 0,
    jobsOutOfSla: 0,
  }),
}));

vi.mock("@/lib/api/analytics-scheduling", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/analytics-scheduling")>();
  return {
    ...actual,
    fetchAlertRules: vi.fn().mockResolvedValue({ items: [] }),
    fetchAlertEvents: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
    fetchReportSchedules: vi.fn().mockResolvedValue({ items: [] }),
  };
});

import { IctLogs } from "@/components/atom/modules/IctLogs";

describe("IctLogs — pestaña Consultas y programación (HU #11610)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchIctQueryFields.mockResolvedValue([]);
    mocks.fetchIctSavedQueries.mockResolvedValue([]);
    mocks.runIctQuery.mockResolvedValue({
      total: 0,
      page: 1,
      pageSize: 25,
      desde: "2026-07-07",
      hasta: "2026-08-05",
      totalPeriodoAnterior: 0,
      filas: [],
      cobertura: [],
    });
  });

  it("muestra las tres pestañas, con Logs activa por defecto", () => {
    render(<IctLogs />);
    expect(screen.getByRole("tab", { name: "Logs" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Alertas ICT" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Consultas" })).toBeInTheDocument();
  });

  it("cambia a la pestaña Consultas y monta la consola de consultas de ICT", async () => {
    const user = userEvent.setup();
    render(<IctLogs />);

    await user.click(screen.getByRole("tab", { name: "Consultas" }));

    expect(screen.getByTestId("ict-queries-tab")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchIctQueryFields).toHaveBeenCalled());
  });

  it("abre el panel de Programación al hacer clic en el botón", async () => {
    const user = userEvent.setup();
    render(<IctLogs />);

    expect(screen.queryByTestId("scheduling-panel")).not.toBeInTheDocument();
    await user.click(screen.getByTestId("ict-abrir-programacion"));

    expect(await screen.findByTestId("scheduling-panel")).toBeInTheDocument();
  });

  it("no ofrece 'ICT · Jobs fuera de SLA' en el selector de tipo cuando el usuario no es SuperAdmin", async () => {
    mocks.isSuperAdmin.mockReturnValue(false);
    const user = userEvent.setup();
    render(<IctLogs />);

    await user.click(screen.getByTestId("ict-abrir-programacion"));
    await screen.findByTestId("scheduling-panel");
    await user.click(screen.getByRole("button", { name: /Nuevo informe/i }));

    const select = screen.getByLabelText("Tipo de informe");
    const options = within(select).getAllByRole("option").map((o) => o.textContent);
    expect(options).toContain("ICT · Pre-trámites con novedades");
    expect(options).not.toContain("ICT · Jobs fuera de SLA");
  });

  it("sí ofrece 'ICT · Jobs fuera de SLA' cuando el usuario es SuperAdmin", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    const user = userEvent.setup();
    render(<IctLogs />);

    await user.click(screen.getByTestId("ict-abrir-programacion"));
    await screen.findByTestId("scheduling-panel");
    await user.click(screen.getByRole("button", { name: /Nuevo informe/i }));

    const select = screen.getByLabelText("Tipo de informe");
    const options = within(select).getAllByRole("option").map((o) => o.textContent);
    expect(options).toContain("ICT · Jobs fuera de SLA");
  });
});

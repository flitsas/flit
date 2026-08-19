// IctReports (HU #11619): módulo nuevo "Reportes ICT" — pestañas de reportes en vivo
// (Novedades/Atascados/Jobs/Webhooks), Consultas personalizadas (HU #11610) y Programación
// (movidas aquí desde IctLogs), con la pestaña "Jobs" restringida a SuperAdmin.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  isSuperAdmin: vi.fn(() => false),
  fetchIctQueryFields: vi.fn(),
  fetchIctSavedQueries: vi.fn(),
  runIctQuery: vi.fn(),
  fetchIctNovedadesReport: vi.fn(),
  fetchIctAtascadosReport: vi.fn(),
  fetchIctJobsReport: vi.fn(),
  fetchIctWebhooksReport: vi.fn(),
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

vi.mock("@/lib/api/ict-reports", () => ({
  fetchIctNovedadesReport: mocks.fetchIctNovedadesReport,
  fetchIctAtascadosReport: mocks.fetchIctAtascadosReport,
  fetchIctJobsReport: mocks.fetchIctJobsReport,
  fetchIctWebhooksReport: mocks.fetchIctWebhooksReport,
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

import { IctReports } from "@/components/atom/modules/IctReports";

describe("IctReports — pestañas en vivo, Consultas y Programación (HU #11619)", () => {
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
    mocks.fetchIctNovedadesReport.mockResolvedValue({
      resumenPorCausa: [{ causa: "Documento ilegible", cantidad: 3, porcentajeTexto: "60%" }],
      detalle: [
        {
          placa: "ABC123",
          vin: null,
          radicado: "R-1",
          comentarios: "Falta soporte",
          registradoEn: "2026-08-01T10:00:00Z",
        },
      ],
      total: 3,
      truncated: false,
    });
    mocks.fetchIctAtascadosReport.mockResolvedValue({ detalle: [], total: 0, truncated: false });
    mocks.fetchIctJobsReport.mockResolvedValue({ resumenPorJob: [], corridasFueraDeSla: [], total: 0, truncated: false });
    mocks.fetchIctWebhooksReport.mockResolvedValue({ detalle: [], total: 0, truncated: false });
  });

  it("muestra Novedades/Atascados/Webhooks/Consultas para un usuario no SuperAdmin, sin Jobs", () => {
    render(<IctReports />);
    expect(screen.getByRole("tab", { name: "Novedades" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Atascados" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Webhooks" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Consultas" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Jobs" })).not.toBeInTheDocument();
  });

  it("SuperAdmin sí ve la pestaña Jobs", () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    render(<IctReports />);
    expect(screen.getByRole("tab", { name: "Jobs" })).toBeInTheDocument();
  });

  it("la pestaña Novedades carga y muestra el resumen y el detalle en vivo", async () => {
    render(<IctReports />);
    await waitFor(() => expect(mocks.fetchIctNovedadesReport).toHaveBeenCalled());
    expect(await screen.findByText("Documento ilegible")).toBeInTheDocument();
    expect(screen.getByText("R-1")).toBeInTheDocument();
  });

  it("cambia a la pestaña Consultas y monta la consola de consultas de ICT", async () => {
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Consultas" }));

    expect(screen.getByTestId("ict-queries-tab")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchIctQueryFields).toHaveBeenCalled());
  });

  it("abre el panel de Programación al hacer clic en el botón", async () => {
    const user = userEvent.setup();
    render(<IctReports />);

    expect(screen.queryByTestId("scheduling-panel")).not.toBeInTheDocument();
    await user.click(screen.getByTestId("ict-reportes-abrir-programacion"));

    expect(await screen.findByTestId("scheduling-panel")).toBeInTheDocument();
  });

  it("no ofrece 'ICT · Detalle de rendimiento de jobs' en el selector de tipo cuando el usuario no es SuperAdmin", async () => {
    mocks.isSuperAdmin.mockReturnValue(false);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByTestId("ict-reportes-abrir-programacion"));
    await screen.findByTestId("scheduling-panel");
    await user.click(screen.getByRole("button", { name: /Nuevo informe/i }));

    const select = screen.getByLabelText("Tipo de informe");
    const options = within(select).getAllByRole("option").map((o) => o.textContent);
    expect(options).toContain("ICT · Detalle de novedades por causa");
    expect(options).not.toContain("ICT · Detalle de rendimiento de jobs");
  });

  it("sí ofrece 'ICT · Detalle de rendimiento de jobs' cuando el usuario es SuperAdmin", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByTestId("ict-reportes-abrir-programacion"));
    await screen.findByTestId("scheduling-panel");
    await user.click(screen.getByRole("button", { name: /Nuevo informe/i }));

    const select = screen.getByLabelText("Tipo de informe");
    const options = within(select).getAllByRole("option").map((o) => o.textContent);
    expect(options).toContain("ICT · Detalle de rendimiento de jobs");
  });

  it("Programar informe desde una consulta guardada abre la Programación con esa consulta preseleccionada", async () => {
    mocks.fetchIctSavedQueries.mockResolvedValue([
      {
        id: "q1",
        nombre: "Mis pendientes",
        descripcion: null,
        deFabrica: false,
        definition: { fechas: { preset: "ultimos_30" }, condiciones: [], columnas: [] },
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: null,
      },
    ]);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Consultas" }));
    await waitFor(() => expect(mocks.fetchIctSavedQueries).toHaveBeenCalled());

    const scheduleButton = await screen.findByRole("button", { name: "Programar informe de Mis pendientes" });
    await user.click(scheduleButton);

    const panel = await screen.findByTestId("scheduling-panel");
    expect(within(panel).getByText(/Mis pendientes/i)).toBeInTheDocument();
  });
});

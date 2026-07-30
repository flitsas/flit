import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AuditoriaTab, SlaTab } from "../../components/atom/modules/_reportes/tabs/SlaAuditoriaTabs";
import { HistoryUnavailableBadge } from "../../components/atom/modules/_reportes/HistoryUnavailableBadge";

const fetchSlaMock = vi.fn();
const fetchAuditMock = vi.fn();
const fetchProceduresMock = vi.fn();
const permsMock = vi.fn();

vi.mock("@/lib/api/reporting-v2", () => ({
  fetchSla: (...args: unknown[]) => fetchSlaMock(...args),
  fetchProcedureAudit: (...args: unknown[]) => fetchAuditMock(...args),
  fetchReportingProcedures: (...args: unknown[]) => fetchProceduresMock(...args),
}));

vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => permsMock(),
}));

describe("HistoryUnavailableBadge", () => {
  it("muestra el mensaje canónico (AC3)", () => {
    render(<HistoryUnavailableBadge />);
    expect(screen.getByTestId("history-unavailable-badge")).toHaveTextContent(
      "Historial no disponible para este trámite",
    );
  });
});

describe("SlaTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    permsMock.mockReturnValue({ permissions: ["reporting.read"], isSuperAdmin: false });
  });

  it("destaca cumplimiento vs incumplimiento (AC1)", async () => {
    fetchSlaMock.mockResolvedValue({
      slaConfigured: true,
      items: [
        {
          procedureType: "traslado",
          transitOfficeName: "OT1",
          slaHours: 72,
          total: 10,
          withinSla: 9,
          outsideSla: 1,
          avgBusinessHours: 20,
          compliancePct: 90,
        },
        {
          procedureType: "matricula",
          transitOfficeName: "OT1",
          slaHours: 48,
          total: 5,
          withinSla: 1,
          outsideSla: 4,
          avgBusinessHours: 60,
          compliancePct: 20,
        },
      ],
    });
    render(<SlaTab from="2026-07-01" to="2026-07-30" />);
    await waitFor(() => expect(screen.getByTestId("sla-row-0")).toBeInTheDocument());
    expect(screen.getByTestId("sla-row-0")).toHaveAttribute("data-compliance", "within");
    expect(screen.getByTestId("sla-row-1")).toHaveAttribute("data-compliance", "outside");
  });

  it("muestra banner sin configuración SLA (AC5)", async () => {
    fetchSlaMock.mockResolvedValue({ slaConfigured: false, items: [] });
    render(<SlaTab from="2026-07-01" to="2026-07-30" />);
    await waitFor(() => expect(screen.getByTestId("sla-not-configured-banner")).toBeInTheDocument());
    expect(screen.getByTestId("sla-not-configured-banner")).toHaveTextContent(
      "Sin configuración de SLA. Configure los objetivos en Ajustes.",
    );
  });
});

describe("AuditoriaTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchProceduresMock.mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 1,
      kpis: { total: 0, approved: 0, rejected: 0, inProgress: 0 },
    });
  });

  it("muestra HistoryUnavailableBadge y no tabla de roles (AC3)", async () => {
    permsMock.mockReturnValue({ permissions: ["reporting.audit"], isSuperAdmin: false });
    fetchAuditMock.mockResolvedValue({
      procedureId: "p1",
      historyAvailable: false,
      entries: [{ changedAt: "2026-07-01T00:00:00Z", historyAvailable: false }],
    });
    const user = userEvent.setup();
    render(<AuditoriaTab from="2026-07-01" to="2026-07-30" />);
    await user.type(screen.getByLabelText("ID del trámite para auditoría"), "p1");
    await waitFor(() => expect(fetchAuditMock).toHaveBeenCalledWith("p1", undefined));
    expect(screen.getByTestId("history-unavailable-badge")).toBeInTheDocument();
    expect(screen.queryByText("Rol")).not.toBeInTheDocument();
  });

  it("sin reporting.audit muestra mensaje y no llama audit (AC4)", () => {
    permsMock.mockReturnValue({ permissions: ["reporting.read"], isSuperAdmin: false });
    render(<AuditoriaTab from="2026-07-01" to="2026-07-30" />);
    expect(screen.getByTestId("auditoria-sin-permiso")).toHaveTextContent(
      "No tienes permiso para ver el historial de auditoría",
    );
    expect(fetchAuditMock).not.toHaveBeenCalled();
  });

  it("SuperAdmin sin tenant muestra selector de empresa (AC6)", () => {
    permsMock.mockReturnValue({ permissions: ["reporting.global"], isSuperAdmin: true });
    render(<AuditoriaTab from="2026-07-01" to="2026-07-30" />);
    expect(screen.getByTestId("auditoria-selector-empresa")).toBeInTheDocument();
    expect(screen.getByTestId("aviso-selecciona-compania")).toBeInTheDocument();
    expect(fetchAuditMock).not.toHaveBeenCalled();
  });
});

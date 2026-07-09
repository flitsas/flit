import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";

// ── Mock del cliente API de programación (sin red) ───────────────────────────
const mocks = vi.hoisted(() => ({
  fetchReportSchedules: vi.fn(),
  createReportSchedule: vi.fn(),
  updateReportSchedule: vi.fn(),
  deleteReportSchedule: vi.fn(),
  fetchAlertRules: vi.fn(),
  createAlertRule: vi.fn(),
  updateAlertRule: vi.fn(),
  deleteAlertRule: vi.fn(),
  fetchAlertEvents: vi.fn(),
}));

vi.mock("@/lib/api/analytics-scheduling", () => mocks);

import { SchedulingPanel } from "@/components/atom/modules/_reportes/scheduling/SchedulingPanel";
import type { AlertRule, ReportSchedule } from "@/lib/api/analytics-scheduling";

const SCHEDULE: ReportSchedule = {
  id: "sch-1",
  name: "Informe semanal OT",
  reportType: "ot",
  frequency: "weekly",
  dayOfWeek: 1,
  dayOfMonth: null,
  sendHour: 7,
  format: "pdf",
  recipients: ["gerencia@empresa.co"],
  isActive: true,
  lastSentAt: null,
};

const RULE: AlertRule = {
  id: "rule-1",
  name: "Rechazo OT alto",
  metric: "rejection_rate_pct",
  operator: "gt",
  threshold: 25,
  windowMinutes: 1440,
  cooldownMinutes: 240,
  recipients: ["alertas@empresa.co"],
  isActive: true,
  lastTriggeredAt: "2026-07-07T12:30:00Z",
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchReportSchedules.mockResolvedValue({ items: [SCHEDULE] });
  mocks.fetchAlertRules.mockResolvedValue({ items: [RULE] });
  mocks.fetchAlertEvents.mockResolvedValue({
    items: [
      {
        id: "evt-1",
        alertRuleId: "rule-1",
        ruleName: "Rechazo OT alto",
        triggeredAt: "2026-07-07T12:30:00Z",
        metricValue: 31.2,
        threshold: 25,
        notified: true,
        message: "La métrica 'Tasa de rechazo (%)' registró 31.2 (mayor que 25).",
      },
    ],
    totalCount: 1,
  });
  mocks.createReportSchedule.mockResolvedValue(SCHEDULE);
  mocks.createAlertRule.mockResolvedValue(RULE);
});

describe("SchedulingPanel — render básico", () => {
  it("no renderiza nada cuando open es false", () => {
    render(<SchedulingPanel open={false} onClose={() => {}} />);
    expect(screen.queryByTestId("scheduling-panel")).not.toBeInTheDocument();
  });

  it("abre con las dos sub-pestañas y lista los informes programados", async () => {
    render(<SchedulingPanel open onClose={() => {}} />);

    expect(screen.getByTestId("scheduling-panel")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /informes programados/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /alertas/i })).toBeInTheDocument();

    const row = await screen.findByTestId("schedule-row");
    expect(row).toHaveTextContent("Informe semanal OT");
    expect(row).toHaveTextContent("Organismo de Tránsito");
    expect(mocks.fetchReportSchedules).toHaveBeenCalled();
  });
});

describe("SchedulingPanel — crear informe programado", () => {
  it("envía el payload correcto a la API al crear", async () => {
    render(<SchedulingPanel open onClose={() => {}} tenantId="t-1" />);
    await screen.findByTestId("schedule-row");

    fireEvent.click(screen.getByRole("button", { name: /nuevo informe/i }));
    const form = screen.getByTestId("schedule-form");

    fireEvent.change(screen.getByLabelText(/nombre/i), { target: { value: "Resumen diario" } });
    fireEvent.change(screen.getByLabelText(/tipo de informe/i), { target: { value: "resumen" } });
    fireEvent.change(screen.getByLabelText(/periodicidad/i), { target: { value: "daily" } });
    fireEvent.change(screen.getByLabelText(/hora de envío/i), { target: { value: "6" } });
    fireEvent.change(screen.getByLabelText(/formato/i), { target: { value: "excel" } });

    const recipientInput = within(form).getByLabelText(/agregar destinatario/i);
    fireEvent.change(recipientInput, { target: { value: "gerencia@empresa.co" } });
    fireEvent.keyDown(recipientInput, { key: "Enter" });
    expect(within(form).getByTestId("recipient-chip")).toHaveTextContent("gerencia@empresa.co");

    fireEvent.click(screen.getByRole("button", { name: /crear informe/i }));

    await waitFor(() => {
      expect(mocks.createReportSchedule).toHaveBeenCalledWith(
        {
          name: "Resumen diario",
          reportType: "resumen",
          frequency: "daily",
          dayOfWeek: null,
          dayOfMonth: null,
          sendHour: 6,
          format: "excel",
          recipients: ["gerencia@empresa.co"],
          isActive: true,
        },
        "t-1",
      );
    });
  });

  it("muestra error en español si el correo es inválido y no llama la API", async () => {
    render(<SchedulingPanel open onClose={() => {}} />);
    await screen.findByTestId("schedule-row");

    fireEvent.click(screen.getByRole("button", { name: /nuevo informe/i }));
    const form = screen.getByTestId("schedule-form");

    const recipientInput = within(form).getByLabelText(/agregar destinatario/i);
    fireEvent.change(recipientInput, { target: { value: "no-es-un-correo" } });
    fireEvent.keyDown(recipientInput, { key: "Enter" });

    const chipError = await within(form).findByTestId("recipients-error");
    expect(chipError).toHaveTextContent("El correo 'no-es-un-correo' no es una dirección válida.");

    // El submit sin destinatarios válidos también falla con mensaje en español.
    fireEvent.change(screen.getByLabelText(/nombre/i), { target: { value: "X" } });
    fireEvent.click(screen.getByRole("button", { name: /crear informe/i }));
    expect(await screen.findByTestId("schedule-form-error")).toHaveTextContent(
      "Debe indicar al menos un destinatario de correo.",
    );
    expect(mocks.createReportSchedule).not.toHaveBeenCalled();
  });
});

describe("SchedulingPanel — alertas", () => {
  it("lista las reglas con métrica en español, condición y cooldown visibles", async () => {
    render(<SchedulingPanel open onClose={() => {}} />);
    await screen.findByTestId("schedule-row");

    fireEvent.click(screen.getByRole("button", { name: /^alertas$/i }));

    const row = await screen.findByTestId("alert-rule-row");
    expect(row).toHaveTextContent("Rechazo OT alto");
    expect(row).toHaveTextContent("Tasa de rechazo (%)");
    expect(row).toHaveTextContent("Mayor que (>) 25");
    expect(within(row).getByTestId("alert-cooldown-cell")).toHaveTextContent("240 min");
    expect(mocks.fetchAlertRules).toHaveBeenCalled();
  });

  it("muestra el historial de disparos paginado", async () => {
    render(<SchedulingPanel open onClose={() => {}} />);
    await screen.findByTestId("schedule-row");

    fireEvent.click(screen.getByRole("button", { name: /^alertas$/i }));
    await screen.findByTestId("alert-rule-row");

    fireEvent.click(screen.getByRole("button", { name: /historial de disparos/i }));

    const history = await screen.findByTestId("alert-events-history");
    const eventRow = await within(history).findByTestId("alert-event-row");
    expect(eventRow).toHaveTextContent("Rechazo OT alto");
    expect(eventRow).toHaveTextContent("31.2");
    expect(eventRow).toHaveTextContent("Sí"); // notificada
    expect(within(history).getByText(/página 1 de 1/i)).toBeInTheDocument();
    expect(mocks.fetchAlertEvents).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, pageSize: 10 }),
    );
  });
});

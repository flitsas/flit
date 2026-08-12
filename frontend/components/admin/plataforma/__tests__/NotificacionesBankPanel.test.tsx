import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificacionesBankPanel } from "@/components/admin/plataforma/NotificacionesBankPanel";

const listNotificationTemplates = vi.fn();
const getTestMailbox = vi.fn();
const updateTestMailbox = vi.fn();
const getNotificationSample = vi.fn();
const sendNotificationTest = vi.fn();

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  listNotificationTemplates: (...a: unknown[]) => listNotificationTemplates(...a),
  getTestMailbox: (...a: unknown[]) => getTestMailbox(...a),
  updateTestMailbox: (...a: unknown[]) => updateTestMailbox(...a),
  getNotificationSample: (...a: unknown[]) => getNotificationSample(...a),
  sendNotificationTest: (...a: unknown[]) => sendNotificationTest(...a),
}));

// Uso de ejemplo: <NotificacionesBankPanel /> carga plantillas + buzón y arma 9 filas
// (8 plantillas del catálogo + Kyverum, informativa).

const sampleTemplates = [
  {
    id: "security.invitation",
    name: "Invitación a la plataforma",
    module: "Security",
    triggers: ["CreateInvitation", "ResendInvitation"],
  },
  {
    id: "security.forgot-password",
    name: "Recuperar contraseña",
    module: "Security",
    triggers: ["ForgotPassword"],
  },
  {
    id: "security.admin-reset-password",
    name: "Restablecimiento por SuperAdmin",
    module: "Security",
    triggers: ["AdminResetPassword"],
  },
  {
    id: "security.welcome-registration",
    name: "Gracias por registrarte",
    module: "Security",
    triggers: ["WelcomeRegistration"],
  },
  {
    id: "analytics.scheduled-report",
    name: "Reporte programado",
    module: "Analytics",
    triggers: ["ScheduledReport"],
  },
  {
    id: "analytics.alert",
    name: "Alerta analítica",
    module: "Analytics",
    triggers: ["Alert"],
  },
  {
    id: "tramites.aprobado",
    name: "Trámite Aprobado",
    module: "Tramites",
    triggers: ["ProcedureStatusChanged"],
  },
  {
    id: "tramites.rechazado",
    name: "Trámite Rechazado",
    module: "Tramites",
    triggers: ["ProcedureStatusChanged"],
  },
];

const sampleMailboxConfigured = {
  isConfigured: true,
  testRecipientEmail: "pruebas@flit.com.co",
  lastTestSentAt: null,
  rowVersion: 1,
};

function mockHappyPath() {
  listNotificationTemplates.mockResolvedValue(sampleTemplates);
  getTestMailbox.mockResolvedValue(sampleMailboxConfigured);
}

describe("NotificacionesBankPanel", { timeout: 15_000 }, () => {
  beforeEach(() => {
    listNotificationTemplates.mockReset();
    getTestMailbox.mockReset();
    updateTestMailbox.mockReset();
    getNotificationSample.mockReset();
    sendNotificationTest.mockReset();
    getTestMailbox.mockResolvedValue(sampleMailboxConfigured);
  });

  it("muestra el estado de carga mientras resuelven las plantillas", async () => {
    let resolveTemplates: (v: typeof sampleTemplates) => void = () => {};
    listNotificationTemplates.mockReturnValue(
      new Promise((resolve) => {
        resolveTemplates = resolve;
      }),
    );

    render(<NotificacionesBankPanel />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    resolveTemplates(sampleTemplates);
    await waitFor(() => expect(screen.queryByTestId("ui-loading")).not.toBeInTheDocument());
  });

  it("muestra el estado de error y reintenta con éxito", async () => {
    listNotificationTemplates.mockRejectedValueOnce(new Error("boom"));

    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    listNotificationTemplates.mockResolvedValue(sampleTemplates);
    await user.click(screen.getByRole("button", { name: /reintentar/i }));

    await waitFor(() => expect(screen.queryByTestId("ui-error")).not.toBeInTheDocument());
    expect(await screen.findByText("Invitación a la plataforma")).toBeInTheDocument();
  });

  it("con catálogo de plantillas vacío conserva solo la fila Kyverum", async () => {
    listNotificationTemplates.mockResolvedValue([]);

    render(<NotificacionesBankPanel />);

    expect(await screen.findByText(/Validación de identidad \(Kyverum Verify\)/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /banco de pruebas \(1\)/i })).toBeInTheDocument();
  });

  it("AC1 — 9 filas (8 plantillas + Kyverum) y buzón arriba de la tabla", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    expect(await screen.findByText("Invitación a la plataforma")).toBeInTheDocument();

    for (const t of sampleTemplates) {
      expect(screen.getByText(t.name)).toBeInTheDocument();
    }
    expect(screen.getByText(/Validación de identidad \(Kyverum Verify\)/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /banco de pruebas \(9\)/i })).toBeInTheDocument();

    const panel = screen.getByTestId("notificaciones-bank-panel");
    const buzonHeading = within(panel).getByRole("heading", { name: /buzón de pruebas/i });
    const tablaHeading = within(panel).getByRole("heading", { name: /banco de pruebas \(9\)/i });
    expect(
      Boolean(
        buzonHeading.compareDocumentPosition(tablaHeading) & Node.DOCUMENT_POSITION_FOLLOWING,
      ),
    ).toBe(true);
  });

  it("AC2 — remitente según módulo; sin selectores de canal/compañía ni bloques de identidad", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");

    expect(screen.queryByRole("combobox", { name: /canal de notificación/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: /^compañía$/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId("notificaciones-identidad-compania")).not.toBeInTheDocument();
    expect(screen.queryByText(/^Vista previa$/i)).not.toBeInTheDocument();

    const securityCount = sampleTemplates.filter((t) => t.module === "Security").length;
    const dualChannelCount = sampleTemplates.length - securityCount;
    expect(screen.getAllByText(/^Colas FLIT$/i).length).toBe(securityCount);
    expect(screen.getAllByText(/Según botón: FLIT o Renting/i).length).toBe(dualChannelCount);
    expect(screen.getByText(/Kyverum Verify \(proveedor externo\)/i)).toBeInTheDocument();
  });

  it("acciones en una columna con grupos FLIT / Renting; Security sin Renting", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");
    expect(screen.getByRole("columnheader", { name: /^acciones$/i })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: /acciones flit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: /acciones renting/i })).not.toBeInTheDocument();

    for (const t of sampleTemplates) {
      const row = screen.getByText(t.name).closest("tr") as HTMLElement;
      const scoped = within(row);
      expect(scoped.getByTestId(`notificaciones-acciones-${t.id}`)).toBeInTheDocument();
      expect(scoped.getByTestId(`notificaciones-acciones-flit-${t.id}`)).toBeInTheDocument();
      expect(scoped.getByRole("button", { name: /preview flit/i })).toBeEnabled();
      expect(scoped.getByRole("button", { name: /enviar flit/i })).toBeEnabled();

      if (t.module === "Security") {
        expect(scoped.queryByTestId(`notificaciones-acciones-renting-${t.id}`)).not.toBeInTheDocument();
        expect(scoped.queryByRole("button", { name: /preview renting/i })).not.toBeInTheDocument();
        expect(scoped.queryByRole("button", { name: /enviar renting/i })).not.toBeInTheDocument();
      } else {
        expect(scoped.getByTestId(`notificaciones-acciones-renting-${t.id}`)).toBeInTheDocument();
        expect(scoped.getByRole("button", { name: /preview renting/i })).toBeEnabled();
        expect(scoped.getByRole("button", { name: /enviar renting/i })).toBeEnabled();
      }
    }
  });

  it("AC3 — la fila Kyverum es informativa: sin preview/enviar, con el motivo visible", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    const kyverumName = await screen.findByText(/Validación de identidad \(Kyverum Verify\)/i);
    const kyverumRow = kyverumName.closest("tr");
    expect(kyverumRow).not.toBeNull();
    const scoped = within(kyverumRow as HTMLElement);

    expect(scoped.queryByRole("button", { name: /preview flit/i })).not.toBeInTheDocument();
    expect(scoped.queryByRole("button", { name: /enviar flit/i })).not.toBeInTheDocument();
    expect(scoped.queryByRole("button", { name: /preview renting/i })).not.toBeInTheDocument();
    expect(scoped.queryByRole("button", { name: /enviar renting/i })).not.toBeInTheDocument();
    expect(
      scoped.getByText(/el correo lo emite el proveedor; flit no controla su contenido/i),
    ).toBeInTheDocument();
  });
});

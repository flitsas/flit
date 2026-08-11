import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificacionesBankPanel } from "@/components/admin/plataforma/NotificacionesBankPanel";
import { ApiError } from "@/lib/api/types";

const listNotificationTemplates = vi.fn();
const listNotificationChannels = vi.fn();
const fetchCompaniesIndex = vi.fn();
const getTestMailbox = vi.fn();
const updateTestMailbox = vi.fn();
const getNotificationSample = vi.fn();
const sendNotificationTest = vi.fn();

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  listNotificationTemplates: (...a: unknown[]) => listNotificationTemplates(...a),
  listNotificationChannels: (...a: unknown[]) => listNotificationChannels(...a),
  getTestMailbox: (...a: unknown[]) => getTestMailbox(...a),
  updateTestMailbox: (...a: unknown[]) => updateTestMailbox(...a),
  getNotificationSample: (...a: unknown[]) => getNotificationSample(...a),
  sendNotificationTest: (...a: unknown[]) => sendNotificationTest(...a),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...a: unknown[]) => fetchCompaniesIndex(...a),
}));

// Uso de ejemplo: <NotificacionesBankPanel /> — HU #11371 cablea "ver en vivo" (AC1/AC2),
// "enviar prueba" (AC3/AC4/AC5) y el buzón de pruebas visible/editable (AC6).

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
];

const sampleChannels = [
  {
    channel: "FLIT_SMTP",
    label: "Colas FLIT",
    isDefault: true,
    isConfigured: true,
    senderEmail: "no-reply@flit.com.co",
    senderName: "FLIT Trámites",
  },
];

const sampleCompanies = {
  data: [
    {
      id: "c1",
      nit: "900123456-1",
      razonSocial: "Renting Colombia S.A.S.",
      code: "RC1",
      tenantType: "renting",
      isTransitOffice: false,
      estadoActivo: true,
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 50,
};

const mailboxConfigured = {
  isConfigured: true,
  testRecipientEmail: "pruebas@flit.com.co",
  lastTestSentAt: "2026-08-01T10:00:00Z",
  rowVersion: 3,
};

const mailboxNotConfigured = {
  isConfigured: false,
  testRecipientEmail: null,
  lastTestSentAt: null,
  rowVersion: 0,
};

function mockCatalog() {
  listNotificationTemplates.mockResolvedValue(sampleTemplates);
  listNotificationChannels.mockResolvedValue(sampleChannels);
  fetchCompaniesIndex.mockResolvedValue(sampleCompanies);
}

async function openRowMenuButton(rowName: string, buttonName: RegExp) {
  const row = (await screen.findByText(rowName)).closest("tr");
  expect(row).not.toBeNull();
  return within(row as HTMLElement).getByRole("button", { name: buttonName });
}

describe("NotificacionesBankPanel — HU #11371", () => {
  beforeEach(() => {
    listNotificationTemplates.mockReset();
    listNotificationChannels.mockReset();
    fetchCompaniesIndex.mockReset();
    getTestMailbox.mockReset();
    updateTestMailbox.mockReset();
    getNotificationSample.mockReset();
    sendNotificationTest.mockReset();
    mockCatalog();
  });

  // ── AC1/AC2 — Ver en vivo, aislado, sin enviar correo ────────────────────

  it("AC1 — pulsar «ver en vivo» muestra el render de muestra sin enviar ningún correo", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    getNotificationSample.mockResolvedValue({
      templateId: "security.invitation",
      subject: "Te invitaron a FLIT",
      html: "<p>Hola</p>",
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /ver en vivo/i);
    await user.click(button);

    expect(await screen.findByText(/te invitaron a flit/i)).toBeInTheDocument();
    expect(getNotificationSample).toHaveBeenCalledWith("security.invitation");
    expect(sendNotificationTest).not.toHaveBeenCalled();
  });

  it("AC2 — el iframe de vista previa usa srcDoc y sandbox restringido sin scripts ni mismo origen", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    getNotificationSample.mockResolvedValue({
      templateId: "security.invitation",
      subject: "Te invitaron a FLIT",
      html: "<p>Hola</p>",
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /ver en vivo/i);
    await user.click(button);

    const iframe = await screen.findByTitle(/vista previa aislada de invitación a la plataforma/i);
    expect(iframe).toHaveAttribute("srcdoc", "<p>Hola</p>");
    expect(iframe).toHaveAttribute("sandbox");
    const sandboxValue = iframe.getAttribute("sandbox") ?? "";
    expect(sandboxValue).not.toContain("allow-scripts");
    expect(sandboxValue).not.toContain("allow-same-origin");
  });

  it("edge case — 404 en la muestra muestra el estado de error con reintento", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    getNotificationSample.mockRejectedValueOnce(new ApiError(404, "No encontrada"));
    getNotificationSample.mockResolvedValueOnce({
      templateId: "security.invitation",
      subject: "Te invitaron a FLIT",
      html: "<p>Hola</p>",
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /ver en vivo/i);
    await user.click(button);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
  });

  // ── AC3/AC4 — Envío con resultado honesto y causa de lista cerrada ───────

  it("AC3 — envío exitoso informa el resultado y advierte que la aceptación no garantiza la entrega", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    sendNotificationTest.mockResolvedValue({
      success: true,
      outcome: "Sent",
      message: "ok",
      templateId: "security.invitation",
      channel: "FLIT_SMTP",
      senderEmail: "no-reply@flit.com.co",
      senderName: "FLIT",
      sentAt: "2026-08-10T12:00:00Z",
      isConsoleTransport: false,
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /enviar prueba/i);
    await user.click(button);

    const result = await screen.findByTestId("notificaciones-enviar-prueba-resultado");
    expect(within(result).getByText(/no garantiza/i)).toBeInTheDocument();
    expect(sendNotificationTest).toHaveBeenCalledWith("security.invitation", "FLIT_SMTP");
  });

  it("AC3 — con transporte de consola declara que no hubo correo real", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    sendNotificationTest.mockResolvedValue({
      success: true,
      outcome: "Sent",
      message: "ok",
      templateId: "security.invitation",
      channel: "FLIT_SMTP",
      senderEmail: null,
      senderName: null,
      sentAt: "2026-08-10T12:00:00Z",
      isConsoleTransport: true,
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /enviar prueba/i);
    await user.click(button);

    expect(
      await screen.findByTestId("notificaciones-enviar-prueba-consola"),
    ).toHaveTextContent(/no se envió un correo real/i);
  });

  it("AC4 — con 429 muestra la causa límite de frecuencia y el tiempo de espera, sin detalle técnico", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    sendNotificationTest.mockRejectedValue(
      new ApiError(429, "Too many requests", {
        error: "limite_frecuencia",
        message: "rate-limit-internal-detail",
        retryAfterSeconds: 270,
      }),
    );
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /enviar prueba/i);
    await user.click(button);

    const errorBlock = await screen.findByTestId("notificaciones-enviar-prueba-error");
    expect(errorBlock).toHaveTextContent(/límite de frecuencia/i);
    expect(errorBlock).toHaveTextContent("270");
    expect(errorBlock).not.toHaveTextContent("rate-limit-internal-detail");
  });

  it("AC4 — con configuracion_incompleta (reemplazo real de 'canal sin adaptador') muestra causa sin mensaje crudo", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    sendNotificationTest.mockRejectedValue(
      new ApiError(400, "Bad request", {
        error: "configuracion_incompleta",
        message: "SMTP host not set for tenant xyz",
      }),
    );
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /enviar prueba/i);
    await user.click(button);

    const errorBlock = await screen.findByTestId("notificaciones-enviar-prueba-error");
    expect(errorBlock).not.toHaveTextContent("SMTP host not set for tenant xyz");
  });

  it("edge case — success:false con outcome TransportFailed (200) se muestra como resultado, no como excepción", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    sendNotificationTest.mockResolvedValue({
      success: false,
      outcome: "TransportFailed",
      message: "SMTP rejected",
      templateId: "security.invitation",
      channel: "FLIT_SMTP",
      senderEmail: "no-reply@flit.com.co",
      senderName: "FLIT",
      sentAt: null,
      isConsoleTransport: false,
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /enviar prueba/i);
    await user.click(button);

    const result = await screen.findByTestId("notificaciones-enviar-prueba-resultado");
    expect(result).not.toHaveTextContent("SMTP rejected");
  });

  // ── AC5 — sin buzón configurado no se ejecuta el envío ───────────────────

  it("AC5 — sin buzón configurado no llama al envío y la pantalla lo solicita", async () => {
    getTestMailbox.mockResolvedValue(mailboxNotConfigured);
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const button = await openRowMenuButton("Invitación a la plataforma", /enviar prueba/i);
    await user.click(button);

    expect(await screen.findByTestId("notificaciones-enviar-prueba-sin-buzon")).toBeInTheDocument();
    expect(sendNotificationTest).not.toHaveBeenCalled();
  });

  // ── AC6 — buzón visible y modificable ────────────────────────────────────

  it("AC6 — un buzón configurado se ve en pantalla", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    render(<NotificacionesBankPanel />);

    const section = await screen.findByTestId("notificaciones-buzon-pruebas");
    expect(within(section).getByText("pruebas@flit.com.co")).toBeInTheDocument();
  });

  it("AC6 — sin buzón configurado la pantalla lo declara y permite configurarlo", async () => {
    getTestMailbox.mockResolvedValue(mailboxNotConfigured);
    render(<NotificacionesBankPanel />);

    expect(
      await screen.findByTestId("notificaciones-buzon-sin-configurar"),
    ).toBeInTheDocument();
  });

  it("AC6 — modificar el buzón y guardar refleja el nuevo valor en pantalla", async () => {
    getTestMailbox.mockResolvedValue(mailboxConfigured);
    updateTestMailbox.mockResolvedValue({
      isConfigured: true,
      testRecipientEmail: "nuevo@flit.com.co",
      lastTestSentAt: null,
      rowVersion: 4,
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const section = await screen.findByTestId("notificaciones-buzon-pruebas");
    await user.click(within(section).getByRole("button", { name: /modificar/i }));

    const input = within(section).getByLabelText(/correo del buzón de pruebas/i);
    await user.clear(input);
    await user.type(input, "nuevo@flit.com.co");
    await user.click(within(section).getByRole("button", { name: /^guardar$/i }));

    await waitFor(() =>
      expect(updateTestMailbox).toHaveBeenCalledWith("nuevo@flit.com.co", 3),
    );
    expect(await within(section).findByText("nuevo@flit.com.co")).toBeInTheDocument();
  });

  it("edge case — 400 correo_invalido al guardar el buzón mantiene el formulario abierto con el error visible", async () => {
    getTestMailbox.mockResolvedValue(mailboxNotConfigured);
    updateTestMailbox.mockRejectedValue(
      new ApiError(400, "Bad request", { error: "correo_invalido" }),
    );
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const section = await screen.findByTestId("notificaciones-buzon-pruebas");
    await user.click(within(section).getByRole("button", { name: /configurar/i }));

    const input = within(section).getByLabelText(/correo del buzón de pruebas/i);
    await user.type(input, "no-es-un-correo");
    await user.click(within(section).getByRole("button", { name: /^guardar$/i }));

    expect(await within(section).findByRole("alert")).toHaveTextContent(/no es válido/i);
  });
});

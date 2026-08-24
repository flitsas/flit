import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificacionesBankPanel } from "@/components/admin/plataforma/NotificacionesBankPanel";

const listNotificationTemplates = vi.fn();
const getTestMailbox = vi.fn();
const getNotificationSample = vi.fn();
const sendNotificationTest = vi.fn();
const listProcedureTypes = vi.fn();

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  listNotificationTemplates: (...a: unknown[]) => listNotificationTemplates(...a),
  getTestMailbox: (...a: unknown[]) => getTestMailbox(...a),
  updateTestMailbox: vi.fn(),
  getNotificationSample: (...a: unknown[]) => getNotificationSample(...a),
  sendNotificationTest: (...a: unknown[]) => sendNotificationTest(...a),
}));

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listProcedureTypes: (...a: unknown[]) => listProcedureTypes(...a),
  },
}));

const templates = [
  {
    id: "tramites.aprobado",
    name: "Trámite Aprobado",
    module: "Tramites",
    triggers: ["ProcedureStatusChanged"],
  },
  {
    id: "security.invitation",
    name: "Invitación a la plataforma",
    module: "Security",
    triggers: ["CreateInvitation"],
  },
];

const activeTypes = [
  {
    id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    code: "CAMBIO_COLOR",
    name: "Cambio de color",
    family: "OTROS",
    publicationStatus: "published",
    isActive: true,
    publishedAt: null,
  },
  {
    id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    code: "TRASPASO_STANDARD",
    name: "Traspaso estándar",
    family: "TRASPASO",
    publicationStatus: "published",
    isActive: true,
    publishedAt: null,
  },
  {
    id: "cccccccc-cccc-cccc-cccc-cccccccccccc",
    code: "ARCHIVADO",
    name: "Tipo inactivo",
    family: "OTROS",
    publicationStatus: "archived",
    isActive: false,
    publishedAt: null,
  },
];

describe("NotificacionesBankPanel — selector de tipo de trámite", () => {
  beforeEach(() => {
    listNotificationTemplates.mockReset();
    getTestMailbox.mockReset();
    getNotificationSample.mockReset();
    sendNotificationTest.mockReset();
    listProcedureTypes.mockReset();
    listNotificationTemplates.mockResolvedValue(templates);
    getTestMailbox.mockResolvedValue({
      isConfigured: true,
      testRecipientEmail: "pruebas@flit.com.co",
      lastTestSentAt: null,
      rowVersion: 1,
    });
    listProcedureTypes.mockResolvedValue(activeTypes);
  });

  it("no llama /muestra al abrir Preview FLIT de Trámite Aprobado hasta elegir tipo", async () => {
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const row = (await screen.findByText("Trámite Aprobado")).closest("tr") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: /preview flit/i }));

    expect(await screen.findByLabelText(/tipo de trámite padre \(familia\)/i)).toBeInTheDocument();
    expect(getNotificationSample).not.toHaveBeenCalled();
    expect(screen.queryByText(/tipo inactivo/i)).not.toBeInTheDocument();
  });

  it("tras elegir tipo activo pide la muestra con procedureTypeId y el nombre del catálogo", async () => {
    getNotificationSample.mockResolvedValue({
      templateId: "tramites.aprobado",
      subject: "asunto",
      html: "<p>el trámite de Cambio de color del vehículo</p>",
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const row = (await screen.findByText("Trámite Aprobado")).closest("tr") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: /preview flit/i }));

    await user.selectOptions(
      await screen.findByLabelText(/tipo de trámite padre \(familia\)/i),
      "OTROS",
    );
    await user.selectOptions(screen.getByLabelText(/^tipo de trámite$/i), activeTypes[0].id);

    await waitFor(() =>
      expect(getNotificationSample).toHaveBeenCalledWith("tramites.aprobado", {
        channel: "FLIT_SMTP",
        procedureTypeId: activeTypes[0].id,
      }),
    );
    const iframe = await screen.findByTestId("notificaciones-vista-previa-iframe");
    expect(iframe.getAttribute("srcdoc")).toContain("Cambio de color");
  });

  it("Invitación no muestra los selects de tipo", async () => {
    getNotificationSample.mockResolvedValue({
      templateId: "security.invitation",
      subject: "Te invitaron",
      html: "<p>Hola</p>",
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const row = (await screen.findByText("Invitación a la plataforma")).closest("tr") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: /preview flit/i }));

    await screen.findByText(/te invitaron/i);
    expect(screen.queryByLabelText(/tipo de trámite padre \(familia\)/i)).not.toBeInTheDocument();
  });

  it("enviar prueba de Aprobado no dispara hasta confirmar tipo", async () => {
    sendNotificationTest.mockResolvedValue({
      success: true,
      outcome: "Sent",
      message: "ok",
      templateId: "tramites.aprobado",
      channel: "FLIT_SMTP",
      senderEmail: null,
      senderName: null,
      sentAt: "2026-08-24T12:00:00Z",
      isConsoleTransport: false,
      recipientDiverted: false,
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const row = (await screen.findByText("Trámite Aprobado")).closest("tr") as HTMLElement;
    await user.click(within(row).getByRole("button", { name: /enviar flit/i }));

    expect(sendNotificationTest).not.toHaveBeenCalled();
    await user.selectOptions(
      await screen.findByLabelText(/tipo de trámite padre \(familia\)/i),
      "OTROS",
    );
    await user.selectOptions(screen.getByLabelText(/^tipo de trámite$/i), activeTypes[0].id);
    await user.click(screen.getByTestId("notificaciones-enviar-prueba-confirmar"));

    await waitFor(() =>
      expect(sendNotificationTest).toHaveBeenCalledWith(
        "tramites.aprobado",
        "FLIT_SMTP",
        activeTypes[0].id,
      ),
    );
  });
});

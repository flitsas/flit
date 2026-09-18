// HU #12431 AC2 — selector "Red con marca" en la consola de plantillas del SuperAdmin. Cubre:
// (1) el selector está presente y por defecto es "FLIT (sin red)", (2) sin selección la petición
// de muestra es IDÉNTICA a como era antes de esta HU (paridad: sin tenantId), (3) con selección
// se agrega tenantId y se muestran `theme.platformName`/`senderName`.
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificacionesBankPanel } from "@/components/admin/plataforma/NotificacionesBankPanel";

const listNotificationTemplates = vi.fn();
const getTestMailbox = vi.fn();
const getNotificationSample = vi.fn();
const sendNotificationTest = vi.fn();
const fetchCompaniesIndex = vi.fn();

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  listNotificationTemplates: (...a: unknown[]) => listNotificationTemplates(...a),
  getTestMailbox: (...a: unknown[]) => getTestMailbox(...a),
  updateTestMailbox: vi.fn(),
  getNotificationSample: (...a: unknown[]) => getNotificationSample(...a),
  sendNotificationTest: (...a: unknown[]) => sendNotificationTest(...a),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...a: unknown[]) => fetchCompaniesIndex(...a),
}));

// Template distinta de `tramites.aprobado`/`tramites.rechazado` a propósito: esas exigen elegir
// un tipo de trámite antes de pedir la muestra (`isTramiteCambioEstadoTemplate`), lo que es
// ortogonal al selector de red que cubre este archivo.
const sampleTemplates = [
  {
    id: "security.invitation",
    name: "Invitación a la plataforma",
    module: "Security",
    triggers: ["CreateInvitation"],
  },
];

const sampleMailbox = {
  isConfigured: true,
  testRecipientEmail: "pruebas@flit.com.co",
  lastTestSentAt: null,
  rowVersion: 1,
};

const networks = {
  data: [
    { id: "tenant-mb-1", nit: "900", razonSocial: "Movilidad Andina", code: "MA1", tenantType: "MARCA_BLANCA", isTransitOffice: false, estadoActivo: true, fechaCreacion: "2026-01-01", rowVersion: 1 },
    { id: "tenant-renting-1", nit: "901", razonSocial: "Renting Otro", code: "R1", tenantType: "RENTING", isTransitOffice: false, estadoActivo: true, fechaCreacion: "2026-01-01", rowVersion: 1 },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 200,
};

beforeEach(() => {
  listNotificationTemplates.mockReset();
  getTestMailbox.mockReset();
  getNotificationSample.mockReset();
  sendNotificationTest.mockReset();
  fetchCompaniesIndex.mockReset();
  listNotificationTemplates.mockResolvedValue(sampleTemplates);
  getTestMailbox.mockResolvedValue(sampleMailbox);
  fetchCompaniesIndex.mockResolvedValue(networks);
});

async function openPreview(user: ReturnType<typeof userEvent.setup>) {
  const row = (await screen.findByText("Invitación a la plataforma")).closest("tr") as HTMLElement;
  await user.click(within(row).getByRole("button", { name: /preview flit/i }));
}

describe("NotificacionesBankPanel — selector Red con marca (AC2)", () => {
  it("muestra el selector con 'FLIT (sin red)' por defecto y solo cabezas MARCA_BLANCA como opciones", async () => {
    render(<NotificacionesBankPanel />);

    const select = await screen.findByLabelText(/red con marca/i);
    expect(select).toHaveValue("");
    expect(within(select).getByText("FLIT (sin red)")).toBeInTheDocument();
    await waitFor(() => expect(within(select).getByText("Movilidad Andina")).toBeInTheDocument());
    expect(within(select).queryByText("Renting Otro")).not.toBeInTheDocument();
  });

  it("sin selección la muestra se pide SIN tenantId (paridad con el comportamiento previo a esta HU)", async () => {
    getNotificationSample.mockResolvedValue({
      templateId: "security.invitation",
      subject: "Tu trámite fue aprobado",
      html: "<p>Hola</p>",
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);
    await screen.findByLabelText(/red con marca/i);

    await openPreview(user);

    await waitFor(() =>
      expect(getNotificationSample).toHaveBeenCalledWith("security.invitation", { channel: "FLIT_SMTP" }),
    );
  });

  it("con una red elegida, la muestra se pide con tenantId y se ve el tema", async () => {
    getNotificationSample.mockResolvedValue({
      templateId: "security.invitation",
      subject: "Tu trámite fue aprobado",
      html: "<p>Hola con marca</p>",
      theme: { kind: "brand", platformName: "Movilidad Andina", version: 2, senderName: "Movilidad Andina" },
    });
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    const select = await screen.findByLabelText(/red con marca/i);
    await waitFor(() => expect(within(select).getByText("Movilidad Andina")).toBeInTheDocument());
    await user.selectOptions(select, "tenant-mb-1");

    await openPreview(user);

    await waitFor(() =>
      expect(getNotificationSample).toHaveBeenCalledWith("security.invitation", {
        channel: "FLIT_SMTP",
        tenantId: "tenant-mb-1",
      }),
    );
    expect(await screen.findByTestId("notificaciones-vista-previa-theme")).toHaveTextContent(
      "Movilidad Andina",
    );
  });
});

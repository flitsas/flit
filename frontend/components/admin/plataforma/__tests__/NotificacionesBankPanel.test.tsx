import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { NotificacionesBankPanel } from "@/components/admin/plataforma/NotificacionesBankPanel";

const listNotificationTemplates = vi.fn();
const listNotificationChannels = vi.fn();
const fetchCompaniesIndex = vi.fn();

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  listNotificationTemplates: (...a: unknown[]) => listNotificationTemplates(...a),
  listNotificationChannels: (...a: unknown[]) => listNotificationChannels(...a),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: (...a: unknown[]) => fetchCompaniesIndex(...a),
}));

// Uso de ejemplo: <NotificacionesBankPanel /> se monta sin props, carga plantillas + canales +
// compañías en paralelo y arma 6 filas (5 plantillas del catálogo + Kyverum, informativa).

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
  {
    channel: "TENANT_API",
    label: "API Renting cliente",
    isDefault: false,
    isConfigured: false,
    senderEmail: null,
    senderName: null,
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
    {
      id: "c2",
      nit: "900987654-2",
      razonSocial: "Autos del Valle",
      code: "AV2",
      tenantType: "standard",
      isTransitOffice: false,
      estadoActivo: true,
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
};

function mockHappyPath() {
  listNotificationTemplates.mockResolvedValue(sampleTemplates);
  listNotificationChannels.mockResolvedValue(sampleChannels);
  fetchCompaniesIndex.mockResolvedValue(sampleCompanies);
}

describe("NotificacionesBankPanel", () => {
  beforeEach(() => {
    listNotificationTemplates.mockReset();
    listNotificationChannels.mockReset();
    fetchCompaniesIndex.mockReset();
  });

  // ── Estados de carga (4 estados obligatorios) ────────────────────────────

  it("muestra el estado de carga mientras resuelven las 3 peticiones", async () => {
    let resolveTemplates: (v: typeof sampleTemplates) => void = () => {};
    listNotificationTemplates.mockReturnValue(
      new Promise((resolve) => {
        resolveTemplates = resolve;
      }),
    );
    listNotificationChannels.mockResolvedValue(sampleChannels);
    fetchCompaniesIndex.mockResolvedValue(sampleCompanies);

    render(<NotificacionesBankPanel />);
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    resolveTemplates(sampleTemplates);
    await waitFor(() => expect(screen.queryByTestId("ui-loading")).not.toBeInTheDocument());
  });

  it("muestra el estado de error y reintenta con éxito", async () => {
    listNotificationTemplates.mockRejectedValueOnce(new Error("boom"));
    listNotificationChannels.mockResolvedValue(sampleChannels);
    fetchCompaniesIndex.mockResolvedValue(sampleCompanies);

    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    listNotificationTemplates.mockResolvedValue(sampleTemplates);
    await user.click(screen.getByRole("button", { name: /reintentar/i }));

    await waitFor(() => expect(screen.queryByTestId("ui-error")).not.toBeInTheDocument());
    expect(await screen.findByText("Invitación a la plataforma")).toBeInTheDocument();
  });

  it("con catálogo de plantillas vacío conserva solo la fila Kyverum (edge case, no altas/bajas inventadas)", async () => {
    listNotificationTemplates.mockResolvedValue([]);
    listNotificationChannels.mockResolvedValue(sampleChannels);
    fetchCompaniesIndex.mockResolvedValue(sampleCompanies);

    render(<NotificacionesBankPanel />);

    expect(await screen.findByText(/Validación de identidad \(Kyverum Verify\)/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /banco de pruebas \(1\)/i })).toBeInTheDocument();
  });

  // ── AC1 ───────────────────────────────────────────────────────────────────

  it("AC1 — estado inicial: canal Colas FLIT y 6 filas (5 plantillas + Kyverum)", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    expect(await screen.findByText("Invitación a la plataforma")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /canal de notificación/i })).toHaveValue("FLIT_SMTP");

    for (const t of sampleTemplates) {
      expect(screen.getByText(t.name)).toBeInTheDocument();
    }
    expect(screen.getByText(/Validación de identidad \(Kyverum Verify\)/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /banco de pruebas \(6\)/i })).toBeInTheDocument();
  });

  // ── AC2 ───────────────────────────────────────────────────────────────────

  it("AC2 — cambiar de canal conserva las mismas 6 filas y cambia el remitente mostrado", async () => {
    mockHappyPath();
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");
    expect(screen.getAllByText(/no-reply@flit\.com\.co/i).length).toBeGreaterThan(0);

    await user.selectOptions(
      screen.getByRole("combobox", { name: /canal de notificación/i }),
      "TENANT_API",
    );

    // Mismas 6 filas, sin altas ni bajas.
    for (const t of sampleTemplates) {
      expect(screen.getByText(t.name)).toBeInTheDocument();
    }
    expect(screen.getByText(/Validación de identidad \(Kyverum Verify\)/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /banco de pruebas \(6\)/i })).toBeInTheDocument();

    // Remitente cambia: ya no aparece el correo de Colas FLIT para las plantillas propias.
    expect(screen.queryAllByText(/no-reply@flit\.com\.co/i)).toHaveLength(0);
    expect(
      screen.getAllByText(/Sin remitente configurado en este canal/i).length,
    ).toBeGreaterThan(0);

    // La fila Kyverum nunca depende del canal seleccionado.
    expect(screen.getByText(/Kyverum Verify \(proveedor externo\)/i)).toBeInTheDocument();
  });

  // ── AC3 ───────────────────────────────────────────────────────────────────

  it("AC3 — la fila Kyverum es informativa: sin ver en vivo/enviar prueba, con el motivo visible", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    const kyverumName = await screen.findByText(/Validación de identidad \(Kyverum Verify\)/i);
    const kyverumRow = kyverumName.closest("tr");
    expect(kyverumRow).not.toBeNull();
    const scoped = within(kyverumRow as HTMLElement);

    expect(scoped.queryByRole("button", { name: /ver en vivo/i })).not.toBeInTheDocument();
    expect(scoped.queryByRole("button", { name: /enviar prueba/i })).not.toBeInTheDocument();
    expect(
      scoped.getByText(/el correo lo emite el proveedor; flit no controla su contenido/i),
    ).toBeInTheDocument();

    // Las filas de plantilla sí muestran los botones (deshabilitados — HU #11371).
    const invitacionRow = screen.getByText("Invitación a la plataforma").closest("tr");
    const scopedInvitacion = within(invitacionRow as HTMLElement);
    expect(scopedInvitacion.getByRole("button", { name: /ver en vivo/i })).toBeDisabled();
    expect(scopedInvitacion.getByRole("button", { name: /enviar prueba/i })).toBeDisabled();
  });

  // ── AC4 ───────────────────────────────────────────────────────────────────

  it("AC4 — cambiar de compañía actualiza la identidad contigua sin alterar la vista previa", async () => {
    mockHappyPath();
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");
    const identityBlock = screen.getByTestId("notificaciones-identidad-compania");
    expect(within(identityBlock).getByText("Renting Colombia S.A.S.")).toBeInTheDocument();
    expect(within(identityBlock).getByText(/900123456-1/)).toBeInTheDocument();

    const previewText = screen.getByText(
      /El contenido de la plantilla es fijo.*no cambia al cambiar de compañía/i,
    ).textContent;

    await user.selectOptions(screen.getByRole("combobox", { name: /^compañía$/i }), "c2");

    expect(within(identityBlock).getByText("Autos del Valle")).toBeInTheDocument();
    expect(within(identityBlock).getByText(/900987654-2/)).toBeInTheDocument();
    expect(within(identityBlock).queryByText("Renting Colombia S.A.S.")).not.toBeInTheDocument();

    // El contenido renderizado de la plantilla (vista previa) permanece idéntico.
    expect(
      screen.getByText(
        /El contenido de la plantilla es fijo.*no cambia al cambiar de compañía/i,
      ).textContent,
    ).toBe(previewText);
  });

  // ── AC5 ───────────────────────────────────────────────────────────────────

  it("AC5 — el bloque de identidad no muestra logotipo ni control para cargarlo", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");
    const identityBlock = screen.getByTestId("notificaciones-identidad-compania");

    expect(within(identityBlock).queryByRole("img")).not.toBeInTheDocument();
    expect(identityBlock.querySelector("input[type='file']")).toBeNull();
    expect(within(identityBlock).queryByText(/logo/i)).not.toBeInTheDocument();
  });

  // ── AC6 ───────────────────────────────────────────────────────────────────

  it("AC6 — sin aviso cuando el canal está configurado (Colas FLIT)", async () => {
    mockHappyPath();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");
    expect(screen.queryByTestId("notificaciones-canal-aviso")).not.toBeInTheDocument();
  });

  it("AC6 — aviso honesto cuando el canal no está configurado, sin mencionar el Bug #11311", async () => {
    mockHappyPath();
    const user = userEvent.setup();
    render(<NotificacionesBankPanel />);

    await screen.findByText("Invitación a la plataforma");
    await user.selectOptions(
      screen.getByRole("combobox", { name: /canal de notificación/i }),
      "TENANT_API",
    );

    const aviso = await screen.findByTestId("notificaciones-canal-aviso");
    expect(aviso).toHaveTextContent(
      "El canal “API Renting cliente” no está configurado ni habilitado en este ambiente. Un envío de prueba por este canal no va a salir hasta que se configure aquí.",
    );
    expect(aviso.textContent?.toLowerCase()).not.toContain("11311");
    expect(aviso.textContent?.toLowerCase()).not.toContain("no altera el transporte");
  });
});

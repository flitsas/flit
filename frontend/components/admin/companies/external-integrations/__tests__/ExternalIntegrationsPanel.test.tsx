// HU #14068 — Pestaña «Integraciones externas» de la ficha de la compañía. Cubre la tabla con sus cuatro estados
// (AC1/AC4), el alta con el secreto mostrado una sola vez (AC2), las acciones con confirmación y los 409 (AC3) y
// que un rol que no es SuperAdmin no ve la pestaña (AC5). La API (HU #14064) es un doble.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ExternalIntegrationsPanel } from "../ExternalIntegrationsPanel";
import { CompanyConfigTabs } from "../../CompanyConfigTabs";
import { ApiError, type TenantSettings } from "@/lib/api/types";
import {
  blockExternalIntegration,
  createExternalIntegration,
  deactivateExternalIntegration,
  fetchExternalIntegrations,
  rotateExternalIntegrationSecret,
  unblockExternalIntegration,
  type ExternalIntegration,
} from "@/lib/api/external-integrations";

vi.mock("@/lib/api/external-integrations", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/external-integrations")>();
  return {
    ...real,
    fetchExternalIntegrations: vi.fn(),
    createExternalIntegration: vi.fn(),
    rotateExternalIntegrationSecret: vi.fn(),
    blockExternalIntegration: vi.fn(),
    unblockExternalIntegration: vi.fn(),
    deactivateExternalIntegration: vi.fn(),
  };
});
vi.mock("@/lib/api/platform", () => ({ listTenantProducts: vi.fn().mockResolvedValue([]), setTenantProduct: vi.fn() }));

const SECRETO = "secreto-de-prueba-0000000000000000000000000";
const COMPANY = "11111111-1111-1111-1111-111111111111";

const activa: ExternalIntegration = {
  id: "int-1",
  clientId: "pn-0123456789abcdef",
  displayName: "Portal Persona Natural",
  purpose: "Consultas del portal",
  scopes: ["platform.tramites.persona_natural"],
  isActive: true,
  mustRotate: false,
  lockedUntil: null,
  lastTokenAt: "2026-10-20T15:00:00Z",
  createdAt: "2026-10-01T10:00:00Z",
  tipo: "persona_natural",
  companyId: COMPANY,
};
const bloqueada: ExternalIntegration = { ...activa, id: "int-2", clientId: "pn-bloqueada", lockedUntil: "9999-12-31T00:00:00Z", lastTokenAt: null };
const desactivada: ExternalIntegration = { ...activa, id: "int-3", clientId: "pn-vieja", isActive: false };

function fila(clientId: string) {
  return screen.getByRole("row", { name: new RegExp(clientId) });
}

function apiError(status: number, error: string) {
  return new ApiError(status, error, { error, message: "x" });
}

describe("ExternalIntegrationsPanel (#14068)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchExternalIntegrations).mockResolvedValue([activa, bloqueada, desactivada]);
  });

  it("AC1: lista las integraciones con estado, alcance de solo lectura y último pase, sin ningún secreto", async () => {
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    const table = await screen.findByRole("table", { name: /integraciones externas de la compañía/i });
    expect(within(table).getAllByRole("columnheader").map((h) => h.textContent)).toEqual([
      expect.stringContaining("Integración"), "Tipo", "Finalidad", "Alcance", expect.stringContaining("Estado"), "Último pase", "Alta", "Acciones",
    ]);
    expect(fetchExternalIntegrations).toHaveBeenCalledWith(COMPANY, expect.anything());
    expect(within(fila("pn-0123456789abcdef")).getByText("Activa")).toBeInTheDocument();
    expect(within(fila("pn-0123456789abcdef")).getByLabelText(/solo lectura/i)).toBeInTheDocument();
    expect(within(fila("pn-bloqueada")).getByLabelText(/bloqueada hasta que se desbloquee/i)).toBeInTheDocument();
    expect(within(fila("pn-vieja")).getByText("Desactivada")).toBeInTheDocument();
    expect(screen.queryByTestId("external-integration-secret")).not.toBeInTheDocument();
  });

  it("AC4: estados vacío y error (con reintento)", async () => {
    vi.mocked(fetchExternalIntegrations).mockResolvedValueOnce([]);
    const { unmount } = render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    expect(await screen.findByText(/no tiene integraciones externas/i)).toBeInTheDocument();
    unmount();

    vi.mocked(fetchExternalIntegrations).mockRejectedValueOnce(new Error("caído"));
    const ue = userEvent.setup();
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    expect(await screen.findByText(/no se pudieron cargar las integraciones externas/i)).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("table")).toBeInTheDocument();
  });

  it("AC4: mientras carga muestra el CarLoaderModal", () => {
    vi.mocked(fetchExternalIntegrations).mockReturnValue(new Promise(() => {}));
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    expect(screen.getByText(/cargando integraciones/i)).toBeInTheDocument();
  });

  it("AC2: el alta (sin integración activa) muestra el secreto una sola vez, se copia y al cerrar desaparece", async () => {
    const ue = userEvent.setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    vi.mocked(fetchExternalIntegrations).mockResolvedValue([desactivada]);
    vi.mocked(createExternalIntegration).mockResolvedValue({
      client: { ...activa, id: "int-9", clientId: "pn-nueva", displayName: "Mi portal" },
      clientSecret: SECRETO,
    });
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /acciones de integraciones externas/i }));
    await ue.click(await screen.findByRole("menuitem", { name: /crear integración persona natural/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(within(dialog).getByLabelText(/nombre/i), "Mi portal");
    await ue.type(within(dialog).getByLabelText(/finalidad/i), "Consultas");
    await ue.click(within(dialog).getByRole("button", { name: /crear integración/i }));

    expect(createExternalIntegration).toHaveBeenCalledWith(COMPANY, {
      tipo: "persona_natural",
      displayName: "Mi portal",
      purpose: "Consultas",
    });
    const secretDialog = await screen.findByRole("dialog", { name: /credenciales de «pn-nueva»/i });
    expect(within(secretDialog).getByTestId("external-integration-secret")).toHaveTextContent(SECRETO);
    expect(within(secretDialog).getByText(/no se podrá ver de nuevo/i)).toBeInTheDocument();
    await ue.click(within(secretDialog).getByRole("button", { name: /copiar secreto/i }));
    expect(writeText).toHaveBeenCalledWith(SECRETO);
    expect(await within(secretDialog).findByText("Copiado")).toBeInTheDocument();

    await ue.click(within(secretDialog).getByRole("button", { name: /ya lo copié/i }));
    await waitFor(() => expect(screen.queryByTestId("external-integration-secret")).not.toBeInTheDocument());
    expect(document.body.textContent).not.toContain(SECRETO);
    // No queda guardado en el almacenamiento del navegador.
    expect(JSON.stringify({ ...window.localStorage })).not.toContain(SECRETO);
    expect(JSON.stringify({ ...window.sessionStorage })).not.toContain(SECRETO);
    expect(fila("pn-nueva")).toBeInTheDocument();
  });

  it("AC2: con una integración activa «Crear» queda deshabilitado con el motivo", async () => {
    const ue = userEvent.setup();
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    await screen.findByRole("table");
    await ue.click(screen.getByRole("button", { name: /acciones de integraciones externas/i }));
    const item = await screen.findByRole("menuitem", { name: /crear integración persona natural/i });
    expect(item).toHaveAttribute("aria-disabled", "true");
  });

  it("AC3: un 409 del alta muestra el motivo claro sin cerrar el formulario ni perder lo escrito", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchExternalIntegrations).mockResolvedValue([]);
    vi.mocked(createExternalIntegration).mockRejectedValue(apiError(409, "integracion_ya_activa"));
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    await screen.findByText(/no tiene integraciones externas/i);
    await ue.click(screen.getByRole("button", { name: /acciones de integraciones externas/i }));
    await ue.click(await screen.findByRole("menuitem", { name: /crear integración persona natural/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(within(dialog).getByLabelText(/nombre/i), "Mi portal");
    await ue.click(within(dialog).getByRole("button", { name: /crear integración/i }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/ya tiene una integración activa/i);
    expect(within(dialog).getByLabelText(/nombre/i)).toHaveValue("Mi portal");
    expect(screen.queryByTestId("external-integration-secret")).not.toBeInTheDocument();
  });

  it("AC3: rotar pide confirmación, permite revocar el anterior al instante y muestra el secreto nuevo una vez", async () => {
    const ue = userEvent.setup();
    vi.mocked(rotateExternalIntegrationSecret).mockResolvedValue({ client: activa, clientSecret: SECRETO });
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    await screen.findByRole("table");

    await ue.click(within(fila("pn-0123456789abcdef")).getByRole("button", { name: /rotar secreto de pn-0123456789abcdef/i }));
    const dialog = await screen.findByRole("dialog", { name: /rotar secreto/i });
    expect(within(dialog).getByText(/24 horas/i)).toBeInTheDocument();
    await ue.click(within(dialog).getByRole("checkbox", { name: /revocar el secreto anterior de inmediato/i }));
    await ue.click(within(dialog).getByRole("button", { name: "Rotar secreto" }));

    expect(rotateExternalIntegrationSecret).toHaveBeenCalledWith(COMPANY, "int-1", true);
    expect(await screen.findByTestId("external-integration-secret")).toHaveTextContent(SECRETO);
  });

  it("AC3: bloquear y desbloquear reflejan el estado en la fila; la desactivada no ofrece acciones", async () => {
    const ue = userEvent.setup();
    vi.mocked(blockExternalIntegration).mockResolvedValue({ ...activa, lockedUntil: "9999-12-31T00:00:00Z" });
    vi.mocked(unblockExternalIntegration).mockResolvedValue({ ...bloqueada, lockedUntil: null });
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    await screen.findByRole("table");

    await ue.click(within(fila("pn-0123456789abcdef")).getByRole("button", { name: /bloquear pn-0123456789abcdef/i }));
    await ue.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Bloquear" }));
    await waitFor(() => expect(within(fila("pn-0123456789abcdef")).getByLabelText(/bloqueada hasta/i)).toBeInTheDocument());

    await ue.click(within(fila("pn-bloqueada")).getByRole("button", { name: /desbloquear pn-bloqueada/i }));
    await ue.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Desbloquear" }));
    await waitFor(() => expect(within(fila("pn-bloqueada")).getByText("Activa")).toBeInTheDocument());

    expect(within(fila("pn-vieja")).getByRole("button", { name: /pn-vieja está desactivada/i })).toBeDisabled();
    // La bloqueada no permite rotar hasta desbloquearla.
    expect(within(fila("pn-0123456789abcdef")).getByRole("button", { name: /rotar secreto de pn-0123456789abcdef/i })).toBeDisabled();
  });

  it("AC3: desactivar pide confirmación y un 409 se muestra en el mismo diálogo", async () => {
    const ue = userEvent.setup();
    vi.mocked(deactivateExternalIntegration).mockRejectedValueOnce(apiError(409, "integracion_inactiva"));
    vi.mocked(deactivateExternalIntegration).mockResolvedValueOnce({ ...activa, isActive: false });
    render(<ExternalIntegrationsPanel companyId={COMPANY} />);
    await screen.findByRole("table");

    await ue.click(within(fila("pn-0123456789abcdef")).getByRole("button", { name: /desactivar pn-0123456789abcdef/i }));
    const dialog = await screen.findByRole("dialog", { name: /desactivar integración/i });
    expect(within(dialog).getByText(/de forma definitiva/i)).toBeInTheDocument();
    await ue.click(within(dialog).getByRole("button", { name: "Desactivar" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/está desactivada/i);

    await ue.click(within(dialog).getByRole("button", { name: "Desactivar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(within(fila("pn-0123456789abcdef")).getByText("Desactivada")).toBeInTheDocument();
  });
});

describe("Pestaña «Integraciones externas» en la ficha (#14068 AC5)", () => {
  const settings: TenantSettings = {
    tenantId: "t1",
    switchesMatricula: {
      allowInitialRegistration: true,
      allowMiscNewVehicles: false,
      onlyOwnVehicles: true,
      onlyOwnVehiclesByFamily: { matriculas: true, traspaso: true, otros: true },
      blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
    },
    baulFirmasActivo: true,
    preasignacionPlacaActiva: false,
    validarSoatConRunt: false,
    enrutamientoSMTP: "FLIT_SMTP",
    notificationTarget: "COMPRADOR",
    metodosRecaudo: [],
  };

  it("el SuperAdmin (slot inyectado) ve la pestaña y su contenido", async () => {
    const ue = userEvent.setup();
    render(
      <CompanyConfigTabs
        settings={settings}
        onSaveSettings={vi.fn()}
        integracionesSlot={<p>contenido de integraciones</p>}
      />,
    );
    await ue.click(screen.getByRole("tab", { name: "Integraciones externas" }));
    expect(screen.getByText("contenido de integraciones")).toBeInTheDocument();
  });

  it("sin slot (rol que no es SuperAdmin) la pestaña no aparece", () => {
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} />);
    expect(screen.queryByRole("tab", { name: "Integraciones externas" })).not.toBeInTheDocument();
  });

  it("el administrador de compañía restringido no la ve aunque el slot llegue", () => {
    render(
      <CompanyConfigTabs
        settings={settings}
        onSaveSettings={vi.fn()}
        restrictedToRepresentatives
        integracionesSlot={<p>no debe verse</p>}
      />,
    );
    expect(screen.queryByRole("tab", { name: "Integraciones externas" })).not.toBeInTheDocument();
  });
});

// HU #12062 — el organismo decide desde el propio detalle. El pie del modal NO ejecuta nada por su
// cuenta: dispara los mismos manejadores que ya usaba la columna de acciones, así que abre los
// mismos diálogos y aplica las mismas reglas. Se prueba desde la sección —y no desde el modal
// suelto— porque lo que hay que demostrar es justamente que ambas vías desembocan en lo mismo.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtBandejaCounters: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtClientProcedure: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

vi.mock("@/lib/api/ot-metrics", () => ({ fetchRejectionReasons: vi.fn().mockResolvedValue([]) }));

vi.mock("@/lib/api/admin-plate-ranges", () => ({
  listPlateDetails: vi.fn().mockResolvedValue([]),
  assignPlateToProcedure: vi.fn(),
  revokeProcedurePlate: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({ fetchMandateSigners: vi.fn() }));
vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

let mockSuperAdmin = false;
vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => mockSuperAdmin };
});

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    analyzeDocument: vi.fn(),
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

import {
  fetchOtBandejaCounters,
  fetchOtBandejaHealth,
  fetchOtClientProcedure,
  fetchOtClientProcedures,
  fetchOtDocuments,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const ENTREGADO: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-101",
  status: "entregado",
  familia: "MATRICULAS",
  createdAt: "2026-08-01T09:00:00Z",
  placa: "ABC123",
};

function prepararBandeja(row: OtClientProcedure) {
  vi.mocked(fetchOtClientProcedures).mockResolvedValue({
    data: [row],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  });
  vi.mocked(fetchOtClientProcedure).mockResolvedValue(row);
}

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

/** Abre el detalle del trámite desde el menú de acciones de su fila. */
async function abrirDetalle(user: ReturnType<typeof userEvent.setup>, radicado: string) {
  await screen.findByText(radicado);
  await user.click(screen.getByRole("button", { name: `Acciones del trámite ${radicado}` }));
  await user.click(await screen.findByRole("menuitem", { name: "Detalle del trámite" }));
  return screen.findByRole("dialog");
}

describe("Detalle OT — decidir desde el modal (HU #12062)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSuperAdmin = false;
    prepararBandeja(ENTREGADO);
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    vi.mocked(fetchOtBandejaCounters).mockResolvedValue({
      transitOfficeResolved: true,
      sinAsignarPlaca: 0,
      conPlacaAsignada: 0,
      aprobados: 0,
      rechazados: 0,
      sinGestion: 1,
    });
    vi.mocked(fetchOtDocuments).mockResolvedValue({
      data: [
        {
          id: "d1",
          tipo: "fur",
          filename: "fur.pdf",
          mimetype: "application/pdf",
          sizeBytes: 100,
          sha256: "a",
          source: "upload",
          uploadedAt: "2026-08-02T10:00:00Z",
        },
      ],
      consolidado: false,
      consolidado_maestro: false,
    });
  });

  it("AC1 — el pie del detalle ofrece rechazar y aprobar", async () => {
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByRole("button", { name: "Rechazar trámite" })).toBeEnabled();
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeEnabled();
  });

  it("AC2 — rechazar abre el diálogo de motivos ya existente, por encima del detalle", async () => {
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await user.click(within(dialog).getByRole("button", { name: "Rechazar trámite" }));

    const motivos = await screen.findByRole("dialog", { name: "Rechazar trámite" });
    // Se apila por encima del detalle (z-[1100]); si no, el clic parecería no hacer nada.
    expect(motivos.className).toContain("z-[1200]");
    // Y conserva su validación: sin observación no se puede confirmar.
    expect(within(motivos).getByRole("button", { name: "Confirmar rechazo" })).toBeDisabled();
  });

  it("AC3 — aprobar abre el diálogo de licencia de tránsito ya existente", async () => {
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    await user.click(within(dialog).getByRole("button", { name: "Aprobar trámite" }));

    const aprobacion = await screen.findByRole("dialog", { name: "Confirmar aprobación" });
    expect(aprobacion.className).toContain("z-[1200]");
    expect(within(aprobacion).getByLabelText(/Licencia de Tránsito/i)).toBeInTheDocument();
  });

  it("AC4 — las acciones de la fila siguen intactas en la bandeja", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-101");
    await user.click(
      screen.getByRole("button", { name: "Acciones del trámite RAD-2026-101" }),
    );

    expect(await screen.findByRole("menuitem", { name: "Aprobar" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Rechazar" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Detalle del trámite" })).toBeInTheDocument();
  });

  it("AC5 — un trámite que aún no admite decisión avisa y deshabilita los botones", async () => {
    // En ruta de placa, el organismo no decide hasta que el gestor termina (Asignado → Terminado).
    const enRuta = { ...ENTREGADO, plateFlowStatus: "asignado" };
    prepararBandeja(enRuta);
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByText(/Pendiente proceso del gestor/i)).toBeInTheDocument();

    const aprobar = within(dialog).getByRole("button", { name: "Aprobar trámite" });
    expect(aprobar).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Rechazar trámite" })).toBeDisabled();
    // El botón deshabilitado apunta a la franja que explica el porqué.
    const describedBy = aprobar.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)).toHaveTextContent(
      /Pendiente proceso del gestor/i,
    );
  });

  it("AC5 — un trámite ya resuelto dice que no admite decisión", async () => {
    prepararBandeja({ ...ENTREGADO, status: "aprobado" });
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(
      within(dialog).getByText(/el organismo solo decide sobre los que tiene entregados/i),
    ).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeDisabled();
  });

  it("AC6 — el SuperAdmin supervisa sin pie de decisión", async () => {
    mockSuperAdmin = true;
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).queryByRole("button", { name: "Aprobar trámite" })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Rechazar trámite" })).not.toBeInTheDocument();
  });

  it("AC6 — en modo Quipux de solo lectura tampoco hay pie", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "quipux",
      quipuxReadOnly: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    const user = userEvent.setup();
    renderSection();

    await waitFor(() => expect(fetchOtProfile).toHaveBeenCalled());
    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).queryByRole("button", { name: "Aprobar trámite" })).not.toBeInTheDocument();
  });

  it("la placa sin preasignar se resuelve desde su propia celda del vehículo", async () => {
    // El prototipo pone la preasignación EN la celda de la placa, no en el pie: es la celda que
    // reporta el problema, así que es donde se espera el remedio.
    prepararBandeja({
      ...ENTREGADO,
      plateFlowStatus: "preasignado",
      placa: null,
      platePreferredLastDigit: "3",
    });
    const user = userEvent.setup();
    renderSection();

    const dialog = await abrirDetalle(user, "RAD-2026-101");
    expect(within(dialog).getByText("Sin preasignar")).toBeInTheDocument();
    expect(within(dialog).getByText("Dígito preferido: 3")).toBeInTheDocument();
    // Sigue sin poder decidirse, pero desde aquí se desbloquea.
    expect(within(dialog).getByRole("button", { name: "Aprobar trámite" })).toBeDisabled();

    await user.click(within(dialog).getByRole("button", { name: "Asignar placa" }));
    expect(await screen.findByRole("dialog", { name: "Asignar placa" })).toBeInTheDocument();
  });

  it("la fila entera abre el detalle, y el menú de acciones no lo dispara", async () => {
    const user = userEvent.setup();
    renderSection();

    // Pulsar cualquier celda de la fila lleva al detalle: es a donde se entra casi siempre.
    await user.click(await screen.findByText("RAD-2026-101"));
    expect(await screen.findByRole("dialog")).toHaveTextContent("Gestión y Aprobación del Trámite");
  });

  it("pulsar el menú de acciones NO abre además el detalle", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-101");
    await user.click(screen.getByRole("button", { name: "Acciones del trámite RAD-2026-101" }));

    // El menú se abre y el detalle NO: si la fila se tragara el clic, elegir «Aprobar» dejaría el
    // detalle abierto por debajo del diálogo.
    expect(await screen.findByRole("menuitem", { name: "Aprobar" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

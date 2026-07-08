// HU #10220 — Vista tenant admin: aprobar/rechazar trámites de clientes.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidado: vi.fn(),
  descargarOtConsolidado: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

// N 03 fix — rol simulable: SuperAdmin supervisa la cola pero no decide (approve/reject
// no soportan su override de organismo); ot_admin conserva las acciones.
let mockSuperAdmin = false;
vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => mockSuperAdmin };
});

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([
      {
        id: "matricula_inicial-type-id",
        code: "matricula_inicial",
        name: "Matrícula inicial",
        family: "MATRICULAS",
        publicationStatus: "published",
        isActive: true,
        publishedAt: null,
      },
    ]),
  },
}));

import {
  adjuntarOtLicenciaTransito,
  approveOtClientProcedure,
  descargarOtConsolidado,
  fetchOtBandejaHealth,
  fetchOtClientProcedures,
  fetchOtProfile,
  generarOtConsolidado,
  rejectOtClientProcedure,
} from "@/lib/api/admin-ot";

const procedure: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-001",
  status: "entregado",
  createdAt: "2026-06-23T09:00:00Z",
};

function renderSection(transitOfficeId?: string) {
  return render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={transitOfficeId} />
    </ToastProvider>,
  );
}

describe("ClientProceduresSection — HU #10220", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSuperAdmin = false;
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [procedure],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    // Por defecto la bandeja está sana (sin entregados huérfanos): el banner no se muestra.
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    vi.mocked(approveOtClientProcedure).mockResolvedValue({
      ...procedure,
      status: "aprobado",
    });
    vi.mocked(rejectOtClientProcedure).mockResolvedValue({
      ...procedure,
      status: "rechazado",
    });
  });

  it("AC1 muestra tabla con columnas requeridas", async () => {
    renderSection();
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Matrícula inicial" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Flota Andina S.A.S." })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Pendiente OT" })).toBeInTheDocument();
  });

  it("AC2 aprobar con confirmación actualiza fila optimistamente", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByRole("button", { name: /Aprobar/i });
    await user.click(screen.getByRole("button", { name: /Aprobar/i }));
    await user.click(screen.getByRole("button", { name: /Confirmar$/i }));
    await waitFor(() => expect(approveOtClientProcedure).toHaveBeenCalledWith("proc-1"));
    expect(screen.getByRole("cell", { name: "Aprobado OT" })).toBeInTheDocument();
  });

  it("AC3 rechazar deshabilita confirmar sin motivo", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByRole("button", { name: /Rechazar/i });
    await user.click(screen.getByRole("button", { name: /Rechazar/i }));
    const confirm = screen.getByRole("button", { name: /Confirmar rechazo/i });
    expect(confirm).toBeDisabled();
    await user.type(screen.getByRole("textbox"), "Documentación incompleta");
    expect(confirm).not.toBeDisabled();
    await user.click(confirm);
    await waitFor(() =>
      expect(rejectOtClientProcedure).toHaveBeenCalledWith("proc-1", {
        reason: "Documentación incompleta",
      }),
    );
  });

  it("AC4 aplica filtro por estado entregado (pendiente OT, N 03)", async () => {
    const user = userEvent.setup();
    renderSection();
    await waitFor(() =>
      expect(fetchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ status: "entregado", pageSize: 20 }),
        expect.anything(),
        undefined,
      ),
    );
    await user.selectOptions(screen.getByLabelText(/Filtrar por tipo de trámite/i), "matricula_inicial-type-id");
    await user.click(screen.getByRole("button", { name: /Aplicar filtros/i }));
    await waitFor(() =>
      expect(fetchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({
          status: "entregado",
          procedureTypeId: "matricula_inicial-type-id",
        }),
        expect.anything(),
        undefined,
      ),
    );
  });

  it("N03 fix — con transitOfficeId scope-a la lista y el perfil (vista SuperAdmin)", async () => {
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    await waitFor(() =>
      expect(fetchOtClientProcedures).toHaveBeenCalledWith(
        expect.objectContaining({ status: "entregado" }),
        expect.anything(),
        { transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001" },
      ),
    );
    expect(fetchOtProfile).toHaveBeenCalledWith(expect.anything(), {
      transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001",
    });
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
  });

  it("N03 fix — SuperAdmin ve la cola pero sin acciones aprobar/rechazar", async () => {
    mockSuperAdmin = true;
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Aprobar$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Rechazar$/i })).not.toBeInTheDocument();
  });

  it("consolidado — generar y ver invocan la API con el trámite", async () => {
    vi.mocked(generarOtConsolidado).mockResolvedValue({
      document: { attachmentId: "att-1", tipo: "consolidado", filename: "c.pdf", sha256: "x" },
    });
    vi.mocked(descargarOtConsolidado).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /Generar consolidado/i }));
    await waitFor(() => expect(generarOtConsolidado).toHaveBeenCalledWith("proc-1", undefined));
    await user.click(screen.getByRole("button", { name: /Ver consolidado/i }));
    await waitFor(() =>
      expect(descargarOtConsolidado).toHaveBeenCalledWith("proc-1", "RAD-2026-001", undefined),
    );
  });

  it("aprobar con LT seleccionada aprueba ANTES de adjuntar la licencia", async () => {
    vi.mocked(adjuntarOtLicenciaTransito).mockResolvedValue({
      id: "att-lt",
      tipo: "licencia_transito",
      filename: "lt.pdf",
      mimetype: "application/pdf",
      sizeBytes: 10,
      sha256: "x",
      source: "ot",
      uploadedAt: "2026-07-06T10:00:00Z",
    });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /^Aprobar$/i }));
    const file = new File(["%PDF-lt"], "lt.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText(/Licencia de Tránsito \(LT\)/i), file);
    await user.click(screen.getByRole("button", { name: /Confirmar$/i }));
    await waitFor(() => expect(adjuntarOtLicenciaTransito).toHaveBeenCalledWith("proc-1", file, undefined));
    expect(approveOtClientProcedure).toHaveBeenCalledWith("proc-1");
    // La aprobación va PRIMERO: el gate de la LT exige el trámite en aprobado (ruta de placa
    // Feature #10587: llega a la aprobación en 'asignado', donde adjuntar antes fallaría).
    const ltOrder = vi.mocked(adjuntarOtLicenciaTransito).mock.invocationCallOrder[0];
    const approveOrder = vi.mocked(approveOtClientProcedure).mock.invocationCallOrder[0];
    expect(approveOrder).toBeLessThan(ltOrder);
  });

  it("fila aprobada ofrece 'Adjuntar LT' para el OT admin", async () => {
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [{ ...procedure, status: "aprobado" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(adjuntarOtLicenciaTransito).mockResolvedValue({
      id: "att-lt",
      tipo: "licencia_transito",
      filename: "lt.pdf",
      mimetype: "application/pdf",
      sizeBytes: 10,
      sha256: "x",
      source: "ot",
      uploadedAt: "2026-07-06T10:00:00Z",
    });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /Adjuntar LT/i }));
    const dialog = screen.getByRole("dialog", { name: /Adjuntar Licencia de Tránsito/i });
    const file = new File(["%PDF-lt"], "lt.pdf", { type: "application/pdf" });
    await user.upload(within(dialog).getByLabelText(/Archivo de la Licencia de Tránsito/i), file);
    await user.click(within(dialog).getByRole("button", { name: /^Adjuntar LT$/i }));
    await waitFor(() =>
      expect(adjuntarOtLicenciaTransito).toHaveBeenCalledWith("proc-1", file, undefined),
    );
  });

  it("HU10541 — muestra banner de diagnóstico cuando hay entregados sin grant", async () => {
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 3,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 2,
      hasDeliveredWithoutGrant: true,
    });
    renderSection();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/2\s+trámites entregados sin grant vigente/i);
    expect(alert).toHaveTextContent(/grant OT.?empresa/i);
  });

  it("HU10541 — no muestra el banner cuando la bandeja está sana", async () => {
    renderSection();
    expect(await screen.findByText("RAD-2026-001")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("HU10541 — el diagnóstico se consulta con el scope del SuperAdmin", async () => {
    renderSection("aaaaaaaa-0001-4000-8000-000000000001");
    await waitFor(() =>
      expect(fetchOtBandejaHealth).toHaveBeenCalledWith(expect.anything(), {
        transitOfficeId: "aaaaaaaa-0001-4000-8000-000000000001",
      }),
    );
  });
});

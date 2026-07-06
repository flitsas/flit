// HU #10220 — Vista tenant admin: aprobar/rechazar trámites de clientes.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
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
  approveOtClientProcedure,
  fetchOtClientProcedures,
  fetchOtProfile,
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
    expect(screen.queryByRole("button", { name: /Aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Rechazar/i })).not.toBeInTheDocument();
  });
});

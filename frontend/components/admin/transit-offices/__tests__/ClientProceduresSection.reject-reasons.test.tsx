// Rechazo con causales del catálogo. El revisor puede marcar VARIAS —un expediente puede llegar
// con improntas borrosas, sin impronta y sin pago de impuestos a la vez— y siempre acompaña una
// observación en texto libre, que es el contexto de quien va a subsanar.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
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
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

const reasonMocks = vi.hoisted(() => ({ fetchRejectionReasons: vi.fn() }));
vi.mock("@/lib/api/ot-metrics", () => ({
  fetchRejectionReasons: reasonMocks.fetchRejectionReasons,
}));

vi.mock("@/lib/api/admin-plate-ranges", () => ({
  listPlateDetails: vi.fn().mockResolvedValue([]),
  assignPlateToProcedure: vi.fn(),
  revokeProcedurePlate: vi.fn(),
}));

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => false };
});

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPublishedProcedureTypes: vi.fn().mockResolvedValue([]) },
}));

import {
  fetchOtBandejaHealth,
  fetchOtClientProcedures,
  fetchOtProfile,
  rejectOtClientProcedure,
} from "@/lib/api/admin-ot";

const entregado: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-101",
  status: "entregado",
  familia: "MATRICULAS",
  createdAt: "2026-08-01T09:00:00Z",
};

const CAUSALES = [
  {
    id: "r1",
    code: "improntas_borrosas",
    description: "Improntas están borrosas",
    modalidad: "matricula_inicial",
    sortOrder: 10,
    isActive: true,
  },
  {
    id: "r2",
    code: "no_pago_impuesto_departamental",
    description: "No pago de impuesto departamental",
    modalidad: "matricula_inicial",
    sortOrder: 20,
    isActive: true,
  },
];

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

async function openRejectModal() {
  const user = userEvent.setup();
  renderSection();
  await screen.findByText("RAD-2026-101");
  await user.click(await screen.findByRole("button", { name: /rechazar/i }));
  await screen.findByRole("dialog", { name: /rechazar trámite/i });
  return user;
}

describe("ClientProceduresSection — rechazo con causales del catálogo", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [entregado],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    vi.mocked(rejectOtClientProcedure).mockResolvedValue({ ...entregado, status: "rechazado" });
    reasonMocks.fetchRejectionReasons.mockResolvedValue(CAUSALES);
  });

  it("pide las causales de la modalidad del trámite", async () => {
    await openRejectModal();

    // Las causales no son intercambiables: «manifiesto de aduana» no aplica a un traspaso.
    await waitFor(() =>
      expect(reasonMocks.fetchRejectionReasons).toHaveBeenCalledWith({
        family: "MATRICULAS",
      }),
    );
  });

  it("envía varias causales junto con la observación", async () => {
    const user = await openRejectModal();

    await screen.findByTestId("reject-reason-catalog");
    await user.click(screen.getByLabelText("Improntas están borrosas"));
    await user.click(screen.getByLabelText("No pago de impuesto departamental"));
    await user.type(
      screen.getByPlaceholderText(/Indica qué debe corregirse/),
      "Las improntas 3 y 4 salen movidas.",
    );
    await user.click(screen.getByRole("button", { name: /confirmar rechazo/i }));

    await waitFor(() =>
      expect(rejectOtClientProcedure).toHaveBeenCalledWith("proc-1", {
        reason: "Las improntas 3 y 4 salen movidas.",
        rejectionReasonIds: ["r1", "r2"],
      }),
    );
  });

  it("permite rechazar sin marcar causales, con solo la observación", async () => {
    const user = await openRejectModal();

    await screen.findByTestId("reject-reason-catalog");
    await user.type(
      screen.getByPlaceholderText(/Indica qué debe corregirse/),
      "Falta el documento del comprador.",
    );
    await user.click(screen.getByRole("button", { name: /confirmar rechazo/i }));

    // Sin causales el campo va ausente, no como lista vacía.
    await waitFor(() =>
      expect(rejectOtClientProcedure).toHaveBeenCalledWith("proc-1", {
        reason: "Falta el documento del comprador.",
        rejectionReasonIds: undefined,
      }),
    );
  });

  it("si el catálogo falla, avisa pero no bloquea el rechazo", async () => {
    reasonMocks.fetchRejectionReasons.mockRejectedValue(new Error("catálogo caído"));
    const user = await openRejectModal();

    // Dejar al revisor sin poder rechazar por un catálogo caído sería peor que perder el dato.
    expect(await screen.findByText(/Puedes rechazar describiendo el motivo/)).toBeInTheDocument();

    await user.type(
      screen.getByPlaceholderText(/Indica qué debe corregirse/),
      "Documento ilegible.",
    );
    await user.click(screen.getByRole("button", { name: /confirmar rechazo/i }));

    await waitFor(() => expect(rejectOtClientProcedure).toHaveBeenCalled());
  });
});

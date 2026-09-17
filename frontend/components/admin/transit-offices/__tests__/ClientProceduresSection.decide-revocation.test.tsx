// HU #12577 (Feature #12565) — [FRONTEND] Revocatoria OT: decidir (aprobar/rechazar) desde el
// dashboard existente. AC1: la fila de un trámite Aprobado ofrece "Decidir revocatoria" con las
// opciones aprobar/rechazar. AC2: rechazar sin motivo bloquea el envío en cliente, sin llamar al
// backend.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  approveOtRevocationRequest: vi.fn(),
  rejectOtRevocationRequest: vi.fn(),
  fetchActiveOtRevocationRequestDetail: vi.fn().mockResolvedValue({
    revocationRequestId: "revreq-1",
    attemptNumber: 1,
    reason: "El comprador desistió de la compra.",
    supportDocumentId: "att-soporte-1",
    requestedAt: "2026-09-16T11:53:00Z",
  }),
  fetchOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => false };
});

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    analyzeDocument: vi.fn(),
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

import {
  approveOtRevocationRequest,
  fetchActiveOtRevocationRequestDetail,
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtClientProcedure,
  fetchOtProfile,
  rejectOtRevocationRequest,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";
import { ApiError } from "@/lib/api/types";

const procedureAprobado: OtClientProcedure = {
  id: "proc-aprobado-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-777",
  status: "aprobado",
  createdAt: "2026-06-23T09:00:00Z",
};

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

async function abrirDecidirRevocatoria(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
  await user.click(await screen.findByRole("menuitem", { name: /Decidir revocatoria/i }));
}

describe("ClientProceduresSection — HU #12577 decidir revocatoria", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
    vi.mocked(searchOtClientProcedures).mockResolvedValue({
      data: [procedureAprobado],
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
  });

  it("AC1 la fila de un trámite Aprobado ofrece Decidir revocatoria con aprobar/rechazar", async () => {
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    expect(await screen.findByText(/Decidir revocatoria/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Aprobar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rechazar" })).toBeInTheDocument();
  });

  it("muestra el motivo y habilita 'Ver documento de soporte' que cargó el gestor", async () => {
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    expect(
      await screen.findByText("El comprador desistió de la compra."),
    ).toBeInTheDocument();
    expect(fetchActiveOtRevocationRequestDetail).toHaveBeenCalledWith(
      "proc-aprobado-1",
      undefined,
    );
    expect(
      screen.getByRole("button", { name: /Ver documento de soporte/i }),
    ).toBeEnabled();
  });

  it("sin documento de soporte, 'Ver documento de soporte' queda deshabilitado", async () => {
    vi.mocked(fetchActiveOtRevocationRequestDetail).mockResolvedValueOnce({
      revocationRequestId: "revreq-2",
      attemptNumber: 1,
      reason: null,
      supportDocumentId: null,
      requestedAt: "2026-09-16T11:53:00Z",
    });
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    expect(await screen.findByText(/no registró un motivo/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Ver documento de soporte/i }),
    ).toBeDisabled();
  });

  it("AC2 rechazar sin motivo bloquea el envío en cliente y no llama al backend", async () => {
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    await user.click(screen.getByRole("button", { name: "Rechazar" }));
    await user.click(screen.getByRole("button", { name: /Confirmar rechazo/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/motivo del rechazo/i);
    expect(rejectOtRevocationRequest).not.toHaveBeenCalled();
  });

  it("AC2 con motivo, el rechazo se envía y el trámite permanece Aprobado", async () => {
    vi.mocked(rejectOtRevocationRequest).mockResolvedValue({
      procedure: null,
      revocationRequestId: "req-1",
      attemptNumber: 1,
      status: "rechazada",
    });
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    await user.click(screen.getByRole("button", { name: "Rechazar" }));
    await user.type(
      screen.getByRole("textbox", { name: /Motivo del rechazo de la revocatoria/i }),
      "El gestor no aportó soporte suficiente",
    );
    await user.click(screen.getByRole("button", { name: /Confirmar rechazo/i }));

    await waitFor(() =>
      expect(rejectOtRevocationRequest).toHaveBeenCalledWith(
        "proc-aprobado-1",
        "El gestor no aportó soporte suficiente",
        undefined,
      ),
    );
    // El modal se cierra tras el éxito.
    await waitFor(() =>
      expect(screen.queryByRole("dialog", { name: "Decidir revocatoria" })).not.toBeInTheDocument(),
    );
  });

  it("tras rechazar, la fila se actualiza sola (sin refrescar la pantalla manualmente)", async () => {
    vi.mocked(rejectOtRevocationRequest).mockResolvedValue({
      procedure: null,
      revocationRequestId: "req-1",
      attemptNumber: 1,
      status: "rechazada",
    });
    // El backend no devuelve el trámite al rechazar (no cambia de estado): la fila se refresca
    // releyéndolo del servidor, ya con el indicativo puesto.
    vi.mocked(fetchOtClientProcedure).mockResolvedValue({
      ...procedureAprobado,
      revocationRequestStatus: "rechazada",
    });
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    await user.click(screen.getByRole("button", { name: "Rechazar" }));
    await user.type(
      screen.getByRole("textbox", { name: /Motivo del rechazo de la revocatoria/i }),
      "El gestor no aportó soporte suficiente",
    );
    await user.click(screen.getByRole("button", { name: /Confirmar rechazo/i }));

    expect(await screen.findByText("Revocatoria rechazada")).toBeInTheDocument();
  });

  it("AC1 aprobar (motivo opcional) actualiza la fila a Revocado", async () => {
    vi.mocked(approveOtRevocationRequest).mockResolvedValue({
      procedure: null,
      revocationRequestId: "req-2",
      attemptNumber: 1,
      status: "aprobada",
    });
    // La fila se actualiza releyendo el trámite del servidor (no del `decision.procedure` de arriba):
    // ver comentario de `confirmDecideRevocation`.
    vi.mocked(fetchOtClientProcedure).mockResolvedValue({
      ...procedureAprobado,
      status: "revocado",
    });
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    // "Aprobar" ya es el modo por defecto al abrir el modal.
    await user.click(screen.getByRole("button", { name: /Aprobar revocatoria/i }));

    await waitFor(() =>
      expect(approveOtRevocationRequest).toHaveBeenCalledWith("proc-aprobado-1", "", undefined),
    );
    expect(await screen.findByRole("status", { name: "Estado: Revocado OT" })).toBeInTheDocument();
  });

  it("si no hay solicitud activa, el backend responde 404 y el error se muestra al OT", async () => {
    vi.mocked(approveOtRevocationRequest).mockRejectedValue(
      new ApiError(404, "No hay una solicitud de revocatoria activa para este trámite", {
        error: "No hay una solicitud de revocatoria activa para este trámite",
      }),
    );
    const user = userEvent.setup();
    renderSection();
    await abrirDecidirRevocatoria(user);

    await user.click(screen.getByRole("button", { name: /Aprobar revocatoria/i }));

    expect(
      await screen.findByText(/No hay una solicitud de revocatoria activa para este trámite/i),
    ).toBeInTheDocument();
  });
});

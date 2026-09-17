// HU #12581 (Feature #12566) — retiro del botón libre "Revocar" del dashboard OT.
// AC1: en un trámite Aprobado la fila ya NO ofrece la acción "Revocar" libre.
// AC2: en su lugar solo queda "Decidir revocatoria" (Feature #12565), la única vía a Revocado.
// AC3: la misma pantalla conserva intactas "Actualizar placa" (HU #12167) y el "Revocar" de la
// PREASIGNACIÓN de placa (HU #10655), que comparte rótulo pero es otra acción.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
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
  fetchActiveOtRevocationRequestDetail: vi.fn(),
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
  fetchOtBandejaFilterFields,
  fetchOtBandejaHealth,
  fetchOtProfile,
  searchOtClientProcedures,
} from "@/lib/api/admin-ot";

/** Aprobado con placa asignada hace un minuto: la ventana de corrección de HU #12167 sigue abierta. */
const aprobadoConPlaca: OtClientProcedure = {
  id: "proc-aprobado-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-777",
  status: "aprobado",
  createdAt: "2026-06-23T09:00:00Z",
  plateAssignedAt: new Date(Date.now() - 60_000).toISOString(),
};

/** Aprobado CON solicitud activa del gestor: el "Given" literal del AC2. */
const aprobadoConSolicitud: OtClientProcedure = {
  ...aprobadoConPlaca,
  id: "proc-aprobado-2",
  referenceNumber: "RAD-2026-779",
  revocationRequestStatus: "solicitada",
};

/** Preasignado: el "Revocar" que sigue vivo es el de la PLACA (HU #10655), no el del trámite. */
const preasignado: OtClientProcedure = {
  id: "proc-preasignado-1",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-778",
  status: "en_revision_ot",
  createdAt: "2026-06-23T09:00:00Z",
  plateFlowStatus: "preasignado",
};

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

async function abrirMenu(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
}

function conFilas(rows: OtClientProcedure[]) {
  vi.mocked(searchOtClientProcedures).mockResolvedValue({
    data: rows,
    totalCount: rows.length,
    page: 1,
    pageSize: 20,
  });
}

describe("ClientProceduresSection — HU #12581 retiro del botón libre de revocación", () => {
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
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
    conFilas([aprobadoConPlaca]);
  });

  it("AC1 un trámite Aprobado ya no ofrece la acción 'Revocar' libre", async () => {
    const user = userEvent.setup();
    renderSection();
    await abrirMenu(user);

    // El menú ya cargó (AC2 comprueba su contenido); aquí lo que importa es la ausencia.
    expect(await screen.findByRole("menuitem", { name: /Decidir revocatoria/i })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /^Revocar$/ })).not.toBeInTheDocument();
  });

  it("AC2 con solicitud pendiente solo queda 'Decidir revocatoria', y accionable", async () => {
    conFilas([aprobadoConSolicitud]);
    const user = userEvent.setup();
    renderSection();
    await abrirMenu(user);

    const revocatorias = await screen.findAllByRole("menuitem", { name: /revoca/i });
    expect(revocatorias.map((m) => m.textContent?.trim())).toEqual(["Decidir revocatoria"]);
    // Habilitada: el gestor sí la solicitó. Sin solicitud se ofrece deshabilitada con motivo
    // (HU #12577) — lo que nunca reaparece es el "Revocar" libre del AC1.
    expect(revocatorias[0]).not.toBeDisabled();
  });

  it("AC3 'Actualizar placa' (HU #12167) sigue presente y habilitada en la misma pantalla", async () => {
    const user = userEvent.setup();
    renderSection();
    await abrirMenu(user);

    const actualizar = await screen.findByRole("menuitem", { name: /Actualizar placa/i });
    expect(actualizar).toBeInTheDocument();
    expect(actualizar).not.toBeDisabled();
  });

  it("AC3 el 'Revocar' de la PREASIGNACIÓN de placa (HU #10655) no se ve afectado", async () => {
    conFilas([preasignado]);
    const user = userEvent.setup();
    renderSection();
    await abrirMenu(user);

    expect(await screen.findByRole("menuitem", { name: /^Revocar$/ })).toBeInTheDocument();
  });
});

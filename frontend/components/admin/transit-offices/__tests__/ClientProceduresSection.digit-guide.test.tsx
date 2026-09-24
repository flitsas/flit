// HU #10805 — en el modal "Asignar placa" del OT, el dígito de preferencia es SOLO una guía.
// HU12852 (Feature #12846, AC1/AC3) — desde esta HU el modal ya no ofrece modo en-rango/fuera-de-
// rango ni una lista de placas para ordenar/marcar con ★: un único campo de placa libre. El
// dígito de preferencia se conserva como sugerencia de texto junto a ese campo (decisión: sigue
// aplicando a la placa libre, ver notas técnicas de la HU).
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
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

const plateMocks = vi.hoisted(() => ({ assignPlateToProcedure: vi.fn() }));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  assignPlateToProcedure: plateMocks.assignPlateToProcedure,
  releaseProcedurePlate: vi.fn(),
  updateProcedurePlate: vi.fn(),
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
  searchOtClientProcedures,
  fetchOtBandejaFilterFields,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const preasignado: OtClientProcedure = {
  id: "proc-7",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-777",
  status: "preasignacion",
  platePreferredLastDigit: "5",
  createdAt: "2026-06-23T09:00:00Z",
};

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

describe("ClientProceduresSection — guía de dígito de preferencia (HU #10805 / HU12852)", () => {
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
      data: [preasignado],
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

  // AC2 (HU #10805) — el modal muestra la guía del dígito, solo como sugerencia.
  it("muestra la guía del dígito de preferencia junto al campo de placa", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-777");
    await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
    await user.click(await screen.findByRole("menuitem", { name: /Asignar placa/i }));

    expect(await screen.findByText(/termina en 5/i)).toBeInTheDocument();
    // HU12852 AC1 — un único campo de placa, sin selector de modo en-rango/fuera-de-rango.
    expect(screen.getByLabelText("Placa")).toBeInTheDocument();
    expect(screen.queryByLabelText(/Placa del rango/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Del rango/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Fuera de rango$/i })).not.toBeInTheDocument();
  });

  // HU12852 AC2 — el payload de asignación ya no depende de un modo elegido en la UI.
  it("HU12852 AC2 — asignar la placa libre llama assignPlateToProcedure sin selector de modo", async () => {
    const user = userEvent.setup();
    plateMocks.assignPlateToProcedure.mockResolvedValue(undefined);
    renderSection();

    await screen.findByText("RAD-2026-777");
    await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
    await user.click(await screen.findByRole("menuitem", { name: /Asignar placa/i }));

    await user.type(screen.getByLabelText("Placa"), "ABC105");
    await user.click(screen.getByRole("button", { name: /^Asignar$/i }));

    expect(plateMocks.assignPlateToProcedure).toHaveBeenCalledWith("proc-7", "ABC105");
  });
});

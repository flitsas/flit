// HU #10805 — en el modal "Asignar placa" del OT, el dígito de preferencia es SOLO una guía:
// las placas del rango que terminan en él se ordenan primero y se marcan con ★; el OT puede
// asignar esa u otra cualquiera.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
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

const plateMocks = vi.hoisted(() => ({ listPlateDetails: vi.fn() }));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  listPlateDetails: plateMocks.listPlateDetails,
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
} from "@/lib/api/admin-ot";

const plate = (id: string, p: string) => ({
  id,
  plateRangeId: "r",
  tenantId: "t",
  transitOfficeId: "o",
  plate: p,
  state: "disponible" as const,
  procedureInstanceId: null,
});

const preasignado: OtClientProcedure = {
  id: "proc-7",
  clientTenantId: "client-tenant-aaaa",
  clientTenantName: "Flota Andina S.A.S.",
  procedureTypeId: "matricula_inicial-type-id",
  procedureTypeName: "Matrícula inicial",
  referenceNumber: "RAD-2026-777",
  status: "entregado",
  plateFlowStatus: "preasignado",
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

describe("ClientProceduresSection — guía de dígito de preferencia (HU #10805)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
    });
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
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
    // ABC105 termina en 5 (el dígito de preferencia); las otras no.
    plateMocks.listPlateDetails.mockResolvedValue([
      plate("1", "ABC101"),
      plate("2", "ABC105"),
      plate("3", "ABC109"),
    ]);
  });

  // AC2 — el modal muestra la guía del dígito y ordena/marca la placa que termina en él.
  it("muestra la guía del dígito y marca/ordena primero la placa que termina en él", async () => {
    const user = userEvent.setup();
    renderSection();

    await screen.findByText("RAD-2026-777");
    // Las acciones de la fila viven en un menú: hay que abrirlo antes de pulsarlas.
    await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
    await user.click(await screen.findByRole("menuitem", { name: /Asignar placa/i }));

    // Guía visible (solo guía, no obliga).
    expect(await screen.findByText(/termina en 5/i)).toBeInTheDocument();

    // La placa que termina en 5 va primero y marcada con ★ (la primera opción real tras el placeholder).
    const select = await screen.findByLabelText(/Placa del rango/i);
    const options = within(select)
      .getAllByRole("option")
      .map((o) => o.textContent?.trim() ?? "");
    expect(options[1]).toContain("ABC105");
    expect(options[1]).toContain("★");
    // Las demás placas siguen disponibles (no se filtran): el OT puede elegir cualquiera.
    expect(options.some((t) => t.includes("ABC101"))).toBe(true);
    expect(options.some((t) => t.includes("ABC109"))).toBe(true);
  });
});

// Búsqueda múltiple por coma — el buscador de la bandeja del OT lo anuncia de forma accesible.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";

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

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

import {
  fetchOtBandejaHealth,
  searchOtClientProcedures,
  fetchOtBandejaFilterFields,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

describe("ClientProceduresSection — ayuda de búsqueda múltiple", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: OT_ID,
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
    vi.mocked(searchOtClientProcedures).mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 20 });
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: OT_ID,
      deliveredTotal: 0,
      deliveredWithGrant: 0,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
  });

  it("el buscador indica varios valores separados por coma y lo describe al lector de pantalla", async () => {
    renderSection();
    const caja = await screen.findByRole("searchbox", { name: "Buscar en la bandeja de trámites" });
    expect(caja).toHaveAttribute("placeholder", "Radicado, placa o VIN · varios separados por coma");
    expect(caja).toHaveAccessibleDescription(/separándolos con coma.*AAA111, BBB222/);
    expect(caja).toHaveAttribute("title", expect.stringMatching(/AAA111, BBB222/));
  });
});

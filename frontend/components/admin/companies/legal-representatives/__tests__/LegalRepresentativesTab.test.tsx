// HU #10904 — Pestaña Representantes legales: lista paginada (4 estados), enmascarado de documento,
// estado de firma/identidad con StatusBadge, eliminación con confirmación y envío de validación de
// identidad. La API se mockea.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { LegalRepresentativesTab } from "../LegalRepresentativesTab";
import type {
  LegalRepresentativeItem,
  LegalRepresentativePage,
} from "@/lib/api/admin-legal-representatives";

vi.mock("@/lib/api/admin-legal-representatives", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-legal-representatives")>();
  return {
    ...actual,
    fetchLegalRepresentatives: vi.fn(),
    createLegalRepresentative: vi.fn(),
    updateLegalRepresentative: vi.fn(),
    deleteLegalRepresentative: vi.fn(),
    sendLegalRepresentativeIdentity: vi.fn(),
  };
});

import {
  deleteLegalRepresentative,
  fetchLegalRepresentatives,
  sendLegalRepresentativeIdentity,
} from "@/lib/api/admin-legal-representatives";

const TENANT = "11111111-1111-1111-1111-111111111111";

const ITEM: LegalRepresentativeItem = {
  id: "rep-1",
  representedCompanyId: "co-1",
  companyDocumentNumber: "900123456-7",
  companyName: "Comercializadora XYZ",
  documentType: "CC",
  documentNumber: "1098765432",
  firstLastName: "Gómez",
  secondLastName: "Ruiz",
  name: "Ana",
  email: "ana@xyz.co",
  address: null,
  city: "Medellín",
  phone: null,
  signatureVaultId: null,
  identityValidationRef: null,
  hasSignatureOrIdentity: false,
  procedureTypeIds: ["019ef140-f24e-78e4-8e6d-97faa44ed7a8"],
  isActive: true,
  createdAt: "2026-06-01T00:00:00Z",
  updatedAt: null,
};

function page(data: LegalRepresentativeItem[]): LegalRepresentativePage {
  return { data, totalCount: data.length, page: 1, pageSize: 20 };
}

function renderTab() {
  return render(
    <ToastProvider>
      <LegalRepresentativesTab tenantId={TENANT} />
    </ToastProvider>,
  );
}

describe("LegalRepresentativesTab (HU #10904)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("muestra el estado vacío cuando no hay representantes", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    renderTab();
    expect(
      await screen.findByText(/aún no tiene representantes legales registrados/i),
    ).toBeInTheDocument();
  });

  it("muestra el estado de error y permite reintentar", async () => {
    vi.mocked(fetchLegalRepresentatives).mockRejectedValueOnce(new Error("boom"));
    renderTab();
    expect(await screen.findByText(/no se pudieron cargar los representantes/i)).toBeInTheDocument();

    vi.mocked(fetchLegalRepresentatives).mockResolvedValueOnce(page([ITEM]));
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("Ana Gómez Ruiz")).toBeInTheDocument();
  });

  it("lista enmascarando el documento y marca el estado sin firma ni identidad", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    renderTab();
    expect(await screen.findByText("Ana Gómez Ruiz")).toBeInTheDocument();
    expect(screen.getByText(/••••5432/)).toBeInTheDocument();
    expect(screen.getByText(/Sin firma ni identidad/i)).toBeInTheDocument();
    // El número completo del documento no debe renderizarse.
    expect(screen.queryByText(/1098765432/)).not.toBeInTheDocument();
  });

  it("envía el correo de validación de identidad", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(sendLegalRepresentativeIdentity).mockResolvedValue({
      id: "val-1",
      status: "PENDING",
      reused: false,
    });
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /^validar identidad$/i }));
    await waitFor(() =>
      expect(sendLegalRepresentativeIdentity).toHaveBeenCalledWith(TENANT, "rep-1"),
    );
  });

  it("elimina un representante tras confirmar en el diálogo", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([ITEM]));
    vi.mocked(deleteLegalRepresentative).mockResolvedValue(undefined);
    renderTab();
    await screen.findByText("Ana Gómez Ruiz");

    await userEvent.click(screen.getByRole("button", { name: /eliminar ana gómez ruiz/i }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: /^eliminar$/i }));

    await waitFor(() => expect(deleteLegalRepresentative).toHaveBeenCalledWith(TENANT, "rep-1"));
  });
});

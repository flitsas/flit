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
    fetchAssignableProcedureTypes: vi.fn(),
    createLegalRepresentative: vi.fn(),
    updateLegalRepresentative: vi.fn(),
    deleteLegalRepresentative: vi.fn(),
    sendLegalRepresentativeIdentity: vi.fn(),
  };
});

import {
  deleteLegalRepresentative,
  fetchAssignableProcedureTypes,
  fetchLegalRepresentatives,
  sendLegalRepresentativeIdentity,
  type AssignableProcedureType,
} from "@/lib/api/admin-legal-representatives";

const TENANT = "11111111-1111-1111-1111-111111111111";

// IDs reales del catálogo (uuidv7 del entorno), NO los hardcodeados que causaban tipo_tramite_inexistente.
const PROC_TYPES: AssignableProcedureType[] = [
  { id: "019f8195-fed1-770a-98ae-295ed59b53d4", code: "TRASPASO_STANDARD", name: "Traspaso" },
  { id: "019f8195-bbdf-72ea-b226-e026826cbfa6", code: "MATRICULA_NUEVA", name: "Matrícula inicial" },
];

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
    // Por defecto el catálogo de tipos de trámite responde con los tipos activos+published.
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue(PROC_TYPES);
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

  it("alimenta el multiselect con los tipos del catálogo del backend (no una lista estática)", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    // Se consultó el catálogo real acotado al tenant.
    await waitFor(() =>
      expect(fetchAssignableProcedureTypes).toHaveBeenCalledWith(TENANT, expect.anything()),
    );

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));

    // El multiselect muestra los nombres devueltos por el backend.
    expect(await screen.findByText("Traspaso")).toBeInTheDocument();
    expect(screen.getByText("Matrícula inicial")).toBeInTheDocument();
  });

  it("muestra el aviso cuando no hay tipos de trámite habilitados", async () => {
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(page([]));
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue([]);
    renderTab();
    await screen.findByText(/aún no tiene representantes legales registrados/i);

    await userEvent.click(screen.getByRole("button", { name: /^nuevo representante$/i }));
    expect(
      await screen.findByText(/no hay tipos de trámite habilitados en el módulo de trámites/i),
    ).toBeInTheDocument();
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

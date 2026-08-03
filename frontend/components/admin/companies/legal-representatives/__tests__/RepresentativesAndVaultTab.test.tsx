// La pestaña "Representantes legales" muestra solo el directorio de personas.
// El baúl de firmas no aparece como sección hermana; la firma se asocia desde la ficha.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { ToastProvider } from "@/components/admin/Toast";
import { RepresentativesAndVaultTab } from "../RepresentativesAndVaultTab";
import type { LegalRepresentativePage } from "@/lib/api/admin-legal-representatives";

vi.mock("@/lib/api/admin-legal-representatives", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-legal-representatives")>();
  return {
    ...actual,
    fetchLegalRepresentatives: vi.fn(),
    fetchAssignableProcedureTypes: vi.fn(),
  };
});

vi.mock("@/lib/api/admin-signature-vault", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-signature-vault")>();
  return {
    ...actual,
    fetchSignatureVault: vi.fn(),
  };
});

import {
  fetchAssignableProcedureTypes,
  fetchLegalRepresentatives,
} from "@/lib/api/admin-legal-representatives";
import { fetchSignatureVault } from "@/lib/api/admin-signature-vault";

const TENANT = "11111111-1111-1111-1111-111111111111";

const emptyPage: LegalRepresentativePage = { data: [], totalCount: 0, page: 1, pageSize: 20 };

function renderTab() {
  return render(
    <ToastProvider>
      <RepresentativesAndVaultTab tenantId={TENANT} />
    </ToastProvider>,
  );
}

describe("RepresentativesAndVaultTab — solo directorio", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(emptyPage);
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue([]);
    vi.mocked(fetchSignatureVault).mockResolvedValue([]);
  });

  it("muestra el directorio de representantes legales", async () => {
    renderTab();
    expect(screen.getByRole("heading", { name: /^representantes legales$/i })).toBeInTheDocument();
    expect(
      await screen.findByText(/aún no tiene representantes legales registrados/i),
    ).toBeInTheDocument();
  });

  it("no muestra la sección suelta del baúl de firmas", async () => {
    renderTab();
    expect(await screen.findByText(/aún no tiene representantes legales registrados/i)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /^baúl de firmas$/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/aún no tiene firmas registradas en el baúl/i)).not.toBeInTheDocument();
    expect(fetchSignatureVault).not.toHaveBeenCalled();
  });
});

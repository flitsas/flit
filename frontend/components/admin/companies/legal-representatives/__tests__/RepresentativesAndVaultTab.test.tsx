// Ajustes HU #10929 — La pestaña "Representantes legales" agrupa DOS secciones diferenciadas:
// "Representantes legales" y "Baúl de firmas". El Baúl solo se muestra si está activo (baulVisible).
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

function renderTab(baulVisible: boolean) {
  return render(
    <ToastProvider>
      <RepresentativesAndVaultTab tenantId={TENANT} baulVisible={baulVisible} />
    </ToastProvider>,
  );
}

describe("RepresentativesAndVaultTab (HU #10929)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchLegalRepresentatives).mockResolvedValue(emptyPage);
    vi.mocked(fetchAssignableProcedureTypes).mockResolvedValue([]);
    vi.mocked(fetchSignatureVault).mockResolvedValue([]);
  });

  it("muestra ambas secciones cuando el baúl está activo", async () => {
    renderTab(true);
    // Encabezados de sección diferenciados.
    expect(screen.getByRole("heading", { name: /^representantes legales$/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /^baúl de firmas$/i })).toBeInTheDocument();
    // Se cargó el contenido de ambas secciones.
    expect(await screen.findByText(/aún no tiene representantes legales registrados/i)).toBeInTheDocument();
    expect(await screen.findByText(/aún no tiene firmas registradas en el baúl/i)).toBeInTheDocument();
  });

  it("oculta la sección del baúl cuando está desactivado", async () => {
    renderTab(false);
    expect(screen.getByRole("heading", { name: /^representantes legales$/i })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /^baúl de firmas$/i })).not.toBeInTheDocument();
    // No se consulta el baúl cuando está oculto.
    expect(fetchSignatureVault).not.toHaveBeenCalled();
  });
});

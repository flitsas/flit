// HU #10644 — Pestaña Baúl de Firmas: lista (4 estados), detalle y anulación con
// confirmación. La API se mockea; el binario de firma nunca se solicita.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { SignatureVaultTab } from "../SignatureVaultTab";
import type { SignatureVaultItem } from "@/lib/api/admin-signature-vault";

vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVault: vi.fn(),
  createSignatureVaultEntry: vi.fn(),
  revokeSignatureVaultEntry: vi.fn(),
}));

import {
  fetchSignatureVault,
  revokeSignatureVaultEntry,
} from "@/lib/api/admin-signature-vault";

const TENANT = "11111111-1111-1111-1111-111111111111";

const ITEM: SignatureVaultItem = {
  id: "sig-1",
  documentType: "CC",
  documentNumber: "1098765432",
  fullName: "Ana Gómez",
  codigoHash: "AB12CD34",
  vigenciaDesde: "2026-01-01",
  vigenciaHasta: "2026-12-31",
  estado: "activa",
  fechaRegistro: "2026-06-01",
  storagePath: "vault/sig-1.png",
};

function renderTab() {
  return render(
    <ToastProvider>
      <SignatureVaultTab tenantId={TENANT} />
    </ToastProvider>,
  );
}

describe("SignatureVaultTab (HU #10644)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("muestra el estado vacío cuando no hay firmas", async () => {
    vi.mocked(fetchSignatureVault).mockResolvedValue([]);
    renderTab();
    expect(await screen.findByText(/aún no tiene firmas registradas/i)).toBeInTheDocument();
  });

  it("muestra el estado de error y permite reintentar", async () => {
    vi.mocked(fetchSignatureVault).mockRejectedValueOnce(new Error("boom"));
    renderTab();
    expect(await screen.findByText(/no se pudieron cargar las firmas/i)).toBeInTheDocument();

    vi.mocked(fetchSignatureVault).mockResolvedValueOnce([ITEM]);
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("Ana Gómez")).toBeInTheDocument();
  });

  it("lista las firmas enmascarando el documento y sin descargar el binario", async () => {
    vi.mocked(fetchSignatureVault).mockResolvedValue([ITEM]);
    renderTab();
    expect(await screen.findByText("Ana Gómez")).toBeInTheDocument();
    expect(screen.getByText(/••••5432/)).toBeInTheDocument();
    expect(screen.getByText("Activa")).toBeInTheDocument();
    // El código hash se muestra; el NIT ya no es una columna del baúl.
    expect(screen.getByText("AB12CD34")).toBeInTheDocument();
    expect(screen.queryByText(/900123456/)).not.toBeInTheDocument();
    // El número completo del documento no debe renderizarse.
    expect(screen.queryByText(/1098765432/)).not.toBeInTheDocument();
  });

  it("anula una firma tras confirmar en el diálogo", async () => {
    vi.mocked(fetchSignatureVault).mockResolvedValue([ITEM]);
    vi.mocked(revokeSignatureVaultEntry).mockResolvedValue(undefined);
    renderTab();
    await screen.findByText("Ana Gómez");

    await userEvent.click(screen.getByRole("button", { name: /anular la firma de ana gómez/i }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: /^anular firma$/i }));

    await waitFor(() => expect(revokeSignatureVaultEntry).toHaveBeenCalledWith(TENANT, "sig-1"));
  });
});

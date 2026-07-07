// ADR-0023 — pestaña "Mandatario" del hub Admin OT: 4 estados de UI, alta con multiselect,
// exclusividad (compañía tomada por otro mandatario deshabilitada + badge) e inactivación.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { MandatariosSection } from "../MandatariosSection";
import { ApiValidationError } from "@/lib/api/types";
import type { MandateSigner, OtCompany } from "@/lib/api/admin-mandate-signers";

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: vi.fn(),
  fetchOtCompanies: vi.fn(),
  createMandateSigner: vi.fn(),
  updateMandateSigner: vi.fn(),
  inactivateMandateSigner: vi.fn(),
  reactivateMandateSigner: vi.fn(),
}));

import {
  fetchMandateSigners,
  fetchOtCompanies,
  createMandateSigner,
  inactivateMandateSigner,
  reactivateMandateSigner,
} from "@/lib/api/admin-mandate-signers";

const companies: OtCompany[] = [
  {
    companyTenantId: "cia-a",
    legalName: "Compañía A S.A.S.",
    isActive: true,
    isEnabled: true,
    assignedSignerId: null,
    assignedSignerName: null,
    assignedSignerHash: null,
  },
  {
    companyTenantId: "cia-b",
    legalName: "Compañía B S.A.S.",
    isActive: true,
    isEnabled: true,
    assignedSignerId: "signer-2",
    assignedSignerName: "Daniel Ríos",
    assignedSignerHash: "abc123",
  },
];

const samuel: MandateSigner = {
  id: "signer-1",
  transitOfficeId: "ot-1",
  fullName: "Samuel Cárdenas",
  documentNumber: "1090123456",
  integrityHash: "a".repeat(64),
  registeredAt: "2026-07-07T12:00:00Z",
  isActive: true,
  companyTenantIds: ["cia-a"],
};

function renderSection() {
  return render(
    <ToastProvider>
      <MandatariosSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

describe("MandatariosSection (ADR-0023)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtCompanies).mockResolvedValue(companies);
  });

  it("estado cargando: muestra el skeleton", () => {
    vi.mocked(fetchMandateSigners).mockReturnValue(new Promise(() => {}));
    renderSection();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("estado vacío: sin mandatarios muestra CTA de registrar", async () => {
    vi.mocked(fetchMandateSigners).mockResolvedValue([]);
    renderSection();
    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Registrar primer mandatario/i })).toBeInTheDocument();
  });

  it("estado error: muestra reintentar y recarga", async () => {
    vi.mocked(fetchMandateSigners).mockRejectedValueOnce(new Error("network"));
    const user = userEvent.setup();
    renderSection();
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    vi.mocked(fetchMandateSigners).mockResolvedValueOnce([samuel]);
    await user.click(screen.getByRole("button", { name: /Reintentar/i }));
    expect(await screen.findByText("Samuel Cárdenas")).toBeInTheDocument();
  });

  it("estado lleno: lista mandatarios con documento enmascarado (PII)", async () => {
    vi.mocked(fetchMandateSigners).mockResolvedValue([samuel]);
    renderSection();
    expect(await screen.findByText("Samuel Cárdenas")).toBeInTheDocument();
    // El número de documento no se muestra completo (Ley 1581): solo los últimos 4.
    expect(screen.getByText("••••3456")).toBeInTheDocument();
    expect(screen.queryByText("1090123456")).not.toBeInTheDocument();
  });

  it("multiselect: la compañía tomada por otro mandatario está deshabilitada con badge", async () => {
    vi.mocked(fetchMandateSigners).mockResolvedValue([samuel]);
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Samuel Cárdenas");

    await user.click(screen.getByRole("button", { name: /Nuevo mandatario/i }));

    const list = screen.getByTestId("ms-company-list");
    expect(within(list).getByLabelText(/Compañía A/i)).not.toBeDisabled();
    // B ya es de Daniel Ríos → deshabilitada + badge.
    expect(within(list).getByLabelText(/Compañía B/i)).toBeDisabled();
    expect(within(list).getByText(/ya tiene mandatario: Daniel Ríos/i)).toBeInTheDocument();
  });

  it("alta: registra un mandatario con nombre, documento y compañía", async () => {
    vi.mocked(fetchMandateSigners).mockResolvedValue([]);
    vi.mocked(createMandateSigner).mockResolvedValue({ id: "new-1", integrityHash: "b".repeat(64) });
    const user = userEvent.setup();
    renderSection();
    await screen.findByTestId("ui-empty");

    await user.click(screen.getByRole("button", { name: /Registrar primer mandatario/i }));
    await user.type(screen.getByLabelText(/Nombre del mandatario/i), "Nuevo Firmante");
    await user.type(screen.getByLabelText(/Número de documento/i), "555999");
    await user.click(screen.getByLabelText(/Compañía A/i));
    await user.click(screen.getByRole("button", { name: /Registrar mandatario/i }));

    await waitFor(() =>
      expect(createMandateSigner).toHaveBeenCalledWith("ot-1", {
        fullName: "Nuevo Firmante",
        documentNumber: "555999",
        companyTenantIds: ["cia-a"],
      }),
    );
  });

  it("alta: muestra el mensaje del backend (422) al chocar la exclusividad", async () => {
    vi.mocked(fetchMandateSigners).mockResolvedValue([]);
    const serverMessage = "La compañía ya tiene un mandatario asignado: Daniel Ríos.";
    vi.mocked(createMandateSigner).mockRejectedValue(
      new ApiValidationError([{ field: "companyTenantIds", message: serverMessage }], 422),
    );
    const user = userEvent.setup();
    renderSection();
    await screen.findByTestId("ui-empty");

    await user.click(screen.getByRole("button", { name: /Registrar primer mandatario/i }));
    await user.type(screen.getByLabelText(/Nombre del mandatario/i), "Colisión");
    await user.type(screen.getByLabelText(/Número de documento/i), "111");
    await user.click(screen.getByLabelText(/Compañía A/i));
    await user.click(screen.getByRole("button", { name: /Registrar mandatario/i }));

    expect(await screen.findByText(serverMessage)).toBeInTheDocument();
  });

  it("inactiva un mandatario (baja lógica que libera compañías)", async () => {
    vi.mocked(fetchMandateSigners).mockResolvedValue([samuel]);
    vi.mocked(inactivateMandateSigner).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Samuel Cárdenas");

    await user.click(screen.getByRole("button", { name: /Inactivar mandatario Samuel Cárdenas/i }));
    await waitFor(() =>
      expect(inactivateMandateSigner).toHaveBeenCalledWith("ot-1", "signer-1"),
    );
  });

  it("un mandatario inactivo sigue visible con badge y acción Reactivar", async () => {
    const inactivo: MandateSigner = { ...samuel, isActive: false, companyTenantIds: [] };
    vi.mocked(fetchMandateSigners).mockResolvedValue([inactivo]);
    vi.mocked(reactivateMandateSigner).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("Samuel Cárdenas");

    // Sigue en la tabla, marcado como inactivo y sin acciones de editar/inactivar.
    expect(screen.getByText("Inactivo")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Inactivar mandatario/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Reactivar mandatario Samuel Cárdenas/i }));
    await waitFor(() =>
      expect(reactivateMandateSigner).toHaveBeenCalledWith("ot-1", "signer-1"),
    );
  });
});

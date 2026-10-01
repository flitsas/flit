// HU #13248 (F9 #13245) - panel de la compañía: estado de la validación propia y «Reenviar validación».
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const fetchCompanyMandateSigners = vi.fn();
const fetchImpact = vi.fn();
const deleteSigner = vi.fn();
const inactivateSigner = vi.fn();
const reactivateSigner = vi.fn();
const resendSigner = vi.fn();

vi.mock("@/lib/api/admin-mandate-signers", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-mandate-signers")>(
    "@/lib/api/admin-mandate-signers",
  );
  return {
    ...actual,
    fetchCompanyMandateSigners: (...a: unknown[]) => fetchCompanyMandateSigners(...a),
    fetchCompanyTransitOffices: vi
      .fn()
      .mockResolvedValue([{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }]),
    fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
    createCompanyMandateSigner: vi.fn(),
    updateCompanyMandateSigner: vi.fn(),
    fetchCompanyMandateSignerImpact: (...a: unknown[]) => fetchImpact(...a),
    deleteCompanyMandateSigner: (...a: unknown[]) => deleteSigner(...a),
    inactivateCompanyMandateSigner: (...a: unknown[]) => inactivateSigner(...a),
    reactivateCompanyMandateSigner: (...a: unknown[]) => reactivateSigner(...a),
    resendCompanyMandateSignerIdentity: (...a: unknown[]) => resendSigner(...a),
  };
});
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import { CompanyMandatariosPanel } from "../CompanyMandatariosPanel";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  return `${header}.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.`;
}

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-1",
    fullName: "Ana Restrepo",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "a".repeat(64),
    email: null,
    userId: null,
    identityStatus: "valid",
    signatureVaultId: "v-1",
    registeredAt: "2026-08-01T10:00:00Z",
    isActive: true,
    companyTenantIds: ["t-1"],
    transitOfficeIds: ["ot-1"],
    signerModel: "natural",
    validityKind: "fixed",
    validityStatus: "vigente",
    origin: "compania",
    puedeEditar: true,
    puedeEliminar: true,
    ...overrides,
  };
}

const SIN_IMPACTO = { hasImpact: false, onlyActiveFor: [], defaults: [], pendingProcedures: 0 };
const CON_IMPACTO = {
  hasImpact: true,
  onlyActiveFor: [{ transitOfficeId: "ot-1", companyTenantId: "t-1" }],
  defaults: [{ kind: "company_rule", transitOfficeId: "ot-1", companyTenantId: "t-1" }],
  pendingProcedures: 3,
};

function renderPanel() {
  return render(
    <ToastProvider>
      <CompanyMandatariosPanel tenantId="t-1" />
    </ToastProvider>,
  );
}

async function abrir(user: ReturnType<typeof userEvent.setup>, nombre: RegExp) {
  await screen.findByRole("table", { name: "Mandatarios de la compañía" });
  await user.click(screen.getByRole("button", { name: nombre }));
  return screen.findByRole("dialog");
}

beforeEach(() => {
  for (const f of [fetchCompanyMandateSigners, fetchImpact, deleteSigner, inactivateSigner, reactivateSigner, resendSigner]) {
    f.mockReset();
  }
  fetchCompanyMandateSigners.mockResolvedValue([signer({ signatureMethod: "biometria", signatureVaultId: null, identityStatus: "none", email: "ana@ejemplo.com" })]);
  window.localStorage.clear();
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({
      sub: "u-1",
      exp: Math.floor(Date.now() / 1000) + 3600,
      role: "AdminCompany",
      roles: [{ id: "r", code: "AdminCompany" }],
    }),
  );
});
afterEach(() => window.localStorage.clear());

describe("HU #13248 — panel de la compañía", () => {
  it("AC1/AC6: la tabla muestra el estado de la validación propia y la acción Reenviar", async () => {
    renderPanel();
    expect(await screen.findByRole("button", { name: /reenviar validación a ana restrepo/i })).toBeEnabled();
    expect(screen.getByTestId("mandatario-validacion-celda")).toHaveTextContent("Pendiente de validación");
  });

  it("AC6: con Baúl no hay estado ni acción", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([signer({ signatureMethod: "baul", signatureVaultId: "v-1" })]);
    renderPanel();
    await screen.findByRole("button", { name: /editar mandatario ana restrepo/i });
    expect(screen.queryByRole("button", { name: /reenviar validación/i })).not.toBeInTheDocument();
    expect(screen.getByTestId("mandatario-validacion-celda")).not.toHaveTextContent("Pendiente");
  });

  it("AC2: reenviar llama a la ruta de la compañía y confirma con el correo", async () => {
    resendSigner.mockResolvedValue({ identity: "sent", validationId: "v-1" });
    const user = userEvent.setup();
    renderPanel();
    await user.click(await screen.findByRole("button", { name: /reenviar validación a ana restrepo/i }));
    await waitFor(() => expect(resendSigner).toHaveBeenCalledWith("t-1", "ms-1", undefined));
    expect(await screen.findByText("Enviamos el enlace de validación a ana@ejemplo.com.")).toBeInTheDocument();
  });

  it("AC2: desde la ficha (editar) el botón reenvía y se ve el estado", async () => {
    resendSigner.mockResolvedValue({ identity: "sent", validationId: "v-1" });
    const user = userEvent.setup();
    renderPanel();
    const dialogo = await abrir(user, /editar mandatario ana restrepo/i);
    expect(within(dialogo).getByTestId("mandatario-validacion-estado")).toHaveTextContent("Pendiente de validación");
    await user.click(within(dialogo).getByRole("button", { name: "Reenviar validación" }));
    await waitFor(() => expect(resendSigner).toHaveBeenCalled());
    expect(await within(dialogo).findByTestId("mandatario-validacion-mensaje")).toHaveTextContent(/ana@ejemplo.com/);
  });

  it("AC5: el candado del organismo (403) se explica sin perder la ficha", async () => {
    resendSigner.mockRejectedValue(new ApiError(403, "x", { code: "mandatario_configurado_por_organismo" }));
    const user = userEvent.setup();
    renderPanel();
    await user.click(await screen.findByRole("button", { name: /reenviar validación a ana restrepo/i }));
    expect(await screen.findByText(/lo configuró el organismo/i)).toBeInTheDocument();
  });

  it("AC7: inactivo no ofrece reenviar y reactivar no dispara ningún envío", async () => {
    fetchCompanyMandateSigners.mockResolvedValue([
      signer({ signatureMethod: "biometria", signatureVaultId: null, identityStatus: "none", isActive: false }),
    ]);
    reactivateSigner.mockResolvedValue({ restoredLinks: [], conflictLinks: [], restoredDefaults: 0 });
    const user = userEvent.setup();
    renderPanel();
    expect(screen.queryByRole("button", { name: /reenviar validación/i })).not.toBeInTheDocument();
    await user.click(await screen.findByRole("button", { name: /reactivar mandatario ana restrepo/i }));
    await waitFor(() => expect(reactivateSigner).toHaveBeenCalled());
    expect(resendSigner).not.toHaveBeenCalled();
  });
});

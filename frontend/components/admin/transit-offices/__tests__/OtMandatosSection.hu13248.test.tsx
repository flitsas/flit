// HU #13248 (F9 #13245) - hub del OT: estado de la validación propia y «Reenviar validación».
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  return `${header}.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.`;
}
function loginAs(roleCode: string) {
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({
      sub: "u-1",
      exp: Math.floor(Date.now() / 1000) + 3600,
      roles: [{ id: "r-1", code: roleCode }],
      role: roleCode,
      entity_type: "TRANSIT_OFFICE",
    }),
  );
}

const fetchMandateOtConfig = vi.fn();
const listCompanyOtMandateRules = vi.fn();
const fetchMandateSigners = vi.fn();
const fetchImpact = vi.fn();
const deleteSigner = vi.fn();
const inactivateSigner = vi.fn();
const reactivateSigner = vi.fn();
const resendSigner = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  fetchMandateOtConfig: (...a: unknown[]) => fetchMandateOtConfig(...a),
  listCompanyOtMandateRules: (...a: unknown[]) => listCompanyOtMandateRules(...a),
  upsertMandateOtConfig: vi.fn(),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
  fetchMandateOtPreview: vi.fn(),
  fetchMandatoTemplatePreview: vi.fn(),
}));
vi.mock("@/lib/api/admin-mandate-signers", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-mandate-signers")>(
    "@/lib/api/admin-mandate-signers",
  );
  return {
    ...actual,
    fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
    fetchMandateSignerImpact: (...a: unknown[]) => fetchImpact(...a),
    deleteMandateSigner: (...a: unknown[]) => deleteSigner(...a),
    inactivateMandateSigner: (...a: unknown[]) => inactivateSigner(...a),
    reactivateMandateSigner: (...a: unknown[]) => reactivateSigner(...a),
    resendMandateSignerIdentity: (...a: unknown[]) => resendSigner(...a),
    updateMandateSigner: vi.fn(),
    createMandateSigner: vi.fn(),
    fetchMandateSignerSignatureImage: vi.fn(),
    fetchCompanyTransitOffices: vi
      .fn()
      .mockResolvedValue([{ transitOfficeId: "ot-1", code: "11001000", name: "OT Bogotá" }]),
    fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
  };
});
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

const office = {
  officeId: "ot-1",
  code: "11001000",
  name: "OT Bogotá",
  templateCode: "generico",
  configuredTemplateCode: "generico",
  requiresForNaturalPerson: false,
  mandataryFamily: "individuo",
  assignmentMode: "open",
  institutionalMandataryName: null,
  institutionalMandataryNit: null,
  chamberCity: null,
  mandatarySigla: null,
  hasExplicitConfig: true,
  rowVersion: 1,
  customTemplateKind: "none",
  customTemplateFileName: null,
  customTemplateBody: null,
  hasCustomTemplate: false,
  defaultMandateSignerId: null,
  defaultMandateSignerName: null,
  defaultMandateSignerDocumentType: null,
  defaultMandateSignerDocumentNumber: null,
  defaultMandateSignerIntegrityHash: null,
};

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-1",
    fullName: "Hugo Mandatario",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "h".repeat(64),
    email: "hugo@ejemplo.com",
    userId: null,
    identityStatus: "none",
    signatureVaultId: null,
    registeredAt: "2026-01-01T00:00:00Z",
    isActive: true,
    companyTenantIds: ["cia-1"],
    transitOfficeIds: ["ot-1"],
    signerModel: "natural",
    signatureMethod: "biometria",
    validityKind: "fixed",
    validityStatus: "vigente",
    origin: "organismo",
    puedeEditar: true,
    puedeEliminar: true,
    ...overrides,
  };
}

function renderSection() {
  return render(
    <ToastProvider>
      <OtMandatosSection transitOfficeId="ot-1" />
    </ToastProvider>,
  );
}

describe("HU #13248 — hub del OT", () => {
  beforeEach(() => {
    for (const f of [fetchImpact, deleteSigner, inactivateSigner, reactivateSigner, resendSigner]) f.mockReset();
    fetchMandateOtConfig.mockReset().mockResolvedValue(office);
    listCompanyOtMandateRules.mockReset().mockResolvedValue([
      {
        companyTenantId: "cia-1",
        companyName: "Gestora de Prueba S.A.S.",
        assignmentMode: "open",
        mandataryFamily: "individuo",
        institutionalMandataryName: null,
        institutionalMandataryNit: null,
        chamberCity: null,
        mandatarySigla: null,
        hasExplicitRule: false,
        defaultMandateSignerId: null,
        companyTaxId: "900123456",
        companyCode: "CIA-1",
        defaultMandateSignerName: "Hugo Mandatario",
        defaultMandateSignerDocumentType: "CC",
        defaultMandateSignerDocumentNumber: "1",
        defaultMandateSignerIntegrityHash: "h",
      },
    ]);
    fetchMandateSigners.mockReset().mockResolvedValue([signer()]);
    window.localStorage.clear();
    loginAs("ot_admin");
  });
  afterEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  it("AC1/AC6: la lista muestra el estado de la validación propia y la acción Reenviar", async () => {
    renderSection();
    const reenviar = await screen.findByRole("button", { name: /reenviar validación a hugo mandatario/i });
    expect(reenviar).toBeEnabled();
    expect(screen.getAllByText("Pendiente de validación").length).toBeGreaterThan(0);
  });

  it("AC6: con Baúl o Persona jurídica no hay acción ni estado de validación", async () => {
    fetchMandateSigners.mockResolvedValue([
      signer({ id: "ms-2", fullName: "Baul Uno", signatureMethod: "baul", signatureVaultId: "v-1" }),
      signer({ id: "ms-3", fullName: "Entidad Dos", signerModel: "juridica", signatureMethod: null }),
    ]);
    renderSection();
    await screen.findByRole("button", { name: /editar mandatario baul uno/i });
    expect(screen.queryByRole("button", { name: /reenviar validación/i })).not.toBeInTheDocument();
    expect(screen.queryByText("Pendiente de validación")).not.toBeInTheDocument();
  });

  it("AC2: reenviar desde la fila llama a la ruta del organismo y confirma con el correo", async () => {
    resendSigner.mockResolvedValue({ identity: "sent", validationId: "v-9" });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /reenviar validación a hugo mandatario/i }));
    await waitFor(() => expect(resendSigner).toHaveBeenCalledWith("ot-1", "ms-1"));
    expect(await screen.findByText("Enviamos el enlace de validación a hugo@ejemplo.com.")).toBeInTheDocument();
  });

  it("AC2: reenviar desde la ficha (editar) usa la misma ruta", async () => {
    resendSigner.mockResolvedValue({ identity: "queued", validationId: "v-9" });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /editar mandatario hugo mandatario/i }));
    const dialogo = await screen.findByRole("dialog");
    expect(within(dialogo).getByTestId("mandatario-validacion-estado")).toHaveTextContent("Pendiente de validación");
    await user.click(within(dialogo).getByRole("button", { name: "Reenviar validación" }));
    await waitFor(() => expect(resendSigner).toHaveBeenCalledWith("ot-1", "ms-1"));
    expect(await within(dialogo).findByText(/Reintentaremos el envío/)).toBeInTheDocument();
  });

  it("AC5: un 422 por correo se muestra en la acción", async () => {
    resendSigner.mockRejectedValue(new ApiError(422, "x", { errors: [{ field: "email" }] }));
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /reenviar validación a hugo mandatario/i }));
    expect(await screen.findByText(/Falta el correo/)).toBeInTheDocument();
  });

  it("AC7: un mandatario inactivo no ofrece reenviar y reactivar no dispara ningún envío", async () => {
    fetchMandateSigners.mockResolvedValue([signer({ isActive: false, validityStatus: "inactivo" })]);
    reactivateSigner.mockResolvedValue({ restoredLinks: [], conflictLinks: [], restoredDefaults: 0 });
    const user = userEvent.setup();
    renderSection();
    expect(screen.queryByRole("button", { name: /reenviar validación/i })).not.toBeInTheDocument();
    await user.click(await screen.findByRole("button", { name: /reactivar mandatario hugo mandatario/i }));
    await waitFor(() => expect(reactivateSigner).toHaveBeenCalled());
    expect(resendSigner).not.toHaveBeenCalled();
  });
});

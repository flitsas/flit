// HU #13140 (Feature F3 #13115) — hub del OT: desactivar, reactivar y eliminar con advertencia de impacto.
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
    email: null,
    userId: null,
    identityStatus: "valid",
    signatureVaultId: "v-1",
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

describe("HU #13140 — hub del OT", () => {
  beforeEach(() => {
    for (const f of [fetchImpact, deleteSigner, inactivateSigner, reactivateSigner]) f.mockReset();
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

  it("AC1: eliminar lista la compañía (con su nombre) y el organismo, y envía confirmarImpacto", async () => {
    fetchImpact.mockResolvedValue({
      hasImpact: true,
      onlyActiveFor: [{ transitOfficeId: "ot-1", companyTenantId: "cia-1" }],
      defaults: [{ kind: "office", transitOfficeId: "ot-1", companyTenantId: null }],
      pendingProcedures: 1,
    });
    deleteSigner.mockResolvedValue({ reassigned: 0, pendingOtDecision: 1 });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /eliminar mandatario hugo mandatario/i }));
    const dialogo = await screen.findByRole("dialog");
    const impacto = await within(dialogo).findByTestId("mandatario-baja-impacto");
    expect(impacto).toHaveTextContent("Gestora de Prueba S.A.S. en OT Bogotá");
    expect(impacto).toHaveTextContent("Mandatario general de OT Bogotá");
    expect(impacto).toHaveTextContent("1 trámite radicado sin aprobar");
    await user.click(within(dialogo).getByRole("button", { name: /sí, eliminar/i }));
    await waitFor(() => expect(deleteSigner).toHaveBeenCalledWith("ot-1", "ms-1", true));
    expect(await screen.findByText(/queda 1 trámite para que el ot decida al aprobar/i)).toBeInTheDocument();
  });

  it("AC2/AC3: desactivar sin impacto confirma corto; cancelar no envía nada", async () => {
    fetchImpact.mockResolvedValue({ hasImpact: false, onlyActiveFor: [], defaults: [], pendingProcedures: 0 });
    inactivateSigner.mockResolvedValue({ reassigned: 0, pendingOtDecision: 0 });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /desactivar mandatario hugo mandatario/i }));
    let dialogo = await screen.findByRole("dialog");
    await within(dialogo).findByText(/¿desactivar a/i);
    await user.click(within(dialogo).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(inactivateSigner).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /desactivar mandatario hugo mandatario/i }));
    dialogo = await screen.findByRole("dialog");
    await user.click(await within(dialogo).findByRole("button", { name: "Desactivar" }));
    await waitFor(() => expect(inactivateSigner).toHaveBeenCalledWith("ot-1", "ms-1"));
  });

  it("AC5: un inactivo se reactiva y un 409 mandatario_activo_existente se explica", async () => {
    fetchMandateSigners.mockResolvedValue([signer({ isActive: false, validityStatus: "inactivo" })]);
    reactivateSigner
      .mockRejectedValueOnce(new ApiError(409, "x", { code: "mandatario_activo_existente" }))
      .mockResolvedValueOnce({ restoredLinks: [], conflictLinks: [], restoredDefaults: 0 });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /reactivar mandatario hugo mandatario/i }));
    expect(await screen.findByText(/ya hay otro mandatario activo para esa compañía y organismo/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /reactivar mandatario hugo mandatario/i }));
    expect(await screen.findByText("Hugo Mandatario vuelve a estar activo.")).toBeInTheDocument();
  });

  it("el Operador OT no ve crear, editar, desactivar ni eliminar", async () => {
    window.localStorage.clear();
    loginAs("gestor_tramites_ot");
    fetchMandateSigners.mockResolvedValue([signer({ puedeEditar: false, puedeEliminar: false })]);
    renderSection();
    const tabla = await screen.findByRole("table", { name: "Mandatarios del organismo" });
    expect(within(tabla).queryByRole("button", { name: /(editar|desactivar|eliminar) mandatario/i })).not.toBeInTheDocument();
  });
});

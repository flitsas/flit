// HU #13181c (Feature F7 #13119) — hub del OT: al EDITAR un mandatario las compañías asociadas viajan en el PUT y
// la propia compañía del mandatario no se ofrece en la lista.
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";
import { ToastProvider } from "@/components/admin/Toast";
import type { CompanyOtMandateRuleView } from "@/lib/api/admin-plataforma-mandatos";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
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
const updateMandateSigner = vi.fn();
const fetchOtAssociableCompanies = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  fetchMandateOtConfig: (...a: unknown[]) => fetchMandateOtConfig(...a),
  listCompanyOtMandateRules: (...a: unknown[]) => listCompanyOtMandateRules(...a),
  upsertMandateOtConfig: vi.fn(),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
  fetchMandateOtPreview: vi.fn(),
  fetchMandatoTemplatePreview: vi.fn(),
}));
vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
  updateMandateSigner: (...a: unknown[]) => updateMandateSigner(...a),
  createMandateSigner: vi.fn(),
  fetchMandateSignerSignatureImage: vi.fn(),
  fetchCompanyTransitOffices: vi
    .fn()
    .mockResolvedValue([{ transitOfficeId: "ot-1", code: "11001000", name: "OT Bogotá" }]),
  fetchRepresentedCompanies: vi.fn().mockResolvedValue([]),
  fetchOtAssociableCompanies: (...a: unknown[]) => fetchOtAssociableCompanies(...a),
}));
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

function companyRow(overrides: Partial<CompanyOtMandateRuleView> = {}): CompanyOtMandateRuleView {
  return {
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
    defaultMandateSignerName: null,
    defaultMandateSignerDocumentType: null,
    defaultMandateSignerDocumentNumber: null,
    defaultMandateSignerIntegrityHash: null,
    rowVersion: null,
    ...overrides,
  };
}

function signer(overrides: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "ms-1",
    transitOfficeId: "ot-1",
    fullName: "Hugo Mandatario",
    documentType: "CC",
    documentNumber: "1020304050",
    integrityHash: "h".repeat(64),
    email: "mandatario@ot.test",
    userId: null,
    identityStatus: "valid",
    signatureVaultId: "v-1",
    registeredAt: "2026-01-01T00:00:00Z",
    isActive: true,
    companyTenantIds: [],
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

describe("HU #13181c — edición del mandatario desde el hub del OT", () => {
  beforeEach(() => {
    fetchMandateOtConfig.mockReset().mockResolvedValue(office);
    listCompanyOtMandateRules.mockReset().mockResolvedValue([]);
    fetchMandateSigners.mockReset().mockResolvedValue([
      signer({
        companyTenantIds: ["cia-1"],
        officeCompanies: [{ transitOfficeId: "ot-1", associatedCompanyTenantIds: ["cia-3"] }],
      }),
    ]);
    updateMandateSigner.mockReset().mockResolvedValue({ id: "ms-1", integrityHash: "h" });
    fetchOtAssociableCompanies.mockReset().mockResolvedValue({
      items: [
        { id: "cia-1", name: "Propia S.A.S.", nit: "900000001" },
        { id: "cia-2", name: "Otra Uno S.A.S.", nit: "900000002" },
        { id: "cia-3", name: "Otra Dos S.A.S.", nit: "900000003" },
      ],
      total: 3,
      page: 1,
      pageSize: 10,
      aplicaSoloASuCompania: false,
    });
    window.localStorage.clear();
    loginAs("ot_admin");
  });
  afterEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  it("el PUT envía officeCompanies con las asociadas marcadas (antes se descartaban)", async () => {
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await user.click(await screen.findByRole("button", { name: /editar mandatario hugo mandatario/i }));
    const dialogo = await screen.findByRole("dialog", { name: /editar mandatario/i });
    await user.click(await within(dialogo).findByLabelText(/Otra Uno S\.A\.S\./));
    await user.click(within(dialogo).getByRole("button", { name: "Guardar" }));

    await waitFor(() => expect(updateMandateSigner).toHaveBeenCalledTimes(1));
    const body = updateMandateSigner.mock.calls[0][2] as {
      officeCompanies?: { transitOfficeId: string; associatedCompanyTenantIds: string[] }[];
    };
    expect(body.officeCompanies).toHaveLength(1);
    expect(body.officeCompanies?.[0].transitOfficeId).toBe("ot-1");
    expect([...(body.officeCompanies?.[0].associatedCompanyTenantIds ?? [])].sort()).toEqual(["cia-2", "cia-3"]);
  });

  it("la lista no ofrece la propia compañía del mandatario", async () => {
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await user.click(await screen.findByRole("button", { name: /editar mandatario hugo mandatario/i }));
    const dialogo = await screen.findByRole("dialog", { name: /editar mandatario/i });
    expect(await within(dialogo).findByLabelText(/Otra Uno S\.A\.S\./)).toBeInTheDocument();
    expect(within(dialogo).queryByLabelText(/Propia S\.A\.S\./)).not.toBeInTheDocument();
  });
});

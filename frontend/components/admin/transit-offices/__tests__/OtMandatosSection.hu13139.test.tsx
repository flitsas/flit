// HU #13139 (Feature F3 #13115) — hub del OT: acciones por banderas del servidor, candado y
// «Sin mandatario» en la lista de compañías.
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
    email: null,
    userId: null,
    identityValidationRef: null,
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

describe("HU #13139 — hub del OT", () => {
  beforeEach(() => {
    fetchMandateOtConfig.mockReset().mockResolvedValue(office);
    listCompanyOtMandateRules.mockReset().mockResolvedValue([]);
    fetchMandateSigners.mockReset().mockResolvedValue([]);
    updateMandateSigner.mockReset();
    window.localStorage.clear();
    loginAs("ot_admin");
  });
  afterEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  it("AC2: el Admin OT ve la acción de editar sobre el de origen organismo, sin candado", async () => {
    fetchMandateSigners.mockResolvedValue([signer()]);
    renderSection();
    const tabla = await screen.findByRole("table", { name: "Mandatarios del organismo" });
    const fila = within(tabla).getByText("Hugo Mandatario").closest("tr") as HTMLElement;
    expect(within(fila).getByRole("button", { name: /editar mandatario hugo mandatario/i })).toBeInTheDocument();
    expect(within(fila).queryByTestId("mandatario-candado")).not.toBeInTheDocument();
  });

  it("AC2: editar abre el formulario y guarda contra la ruta del organismo", async () => {
    fetchMandateSigners.mockResolvedValue([signer()]);
    updateMandateSigner.mockResolvedValue({ id: "ms-1", integrityHash: "h" });
    const user = userEvent.setup();
    renderSection();
    await user.click(await screen.findByRole("button", { name: /editar mandatario hugo mandatario/i }));
    const dialogo = await screen.findByRole("dialog", { name: /editar mandatario/i });
    await user.click(within(dialogo).getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(updateMandateSigner).toHaveBeenCalledTimes(1));
    expect(updateMandateSigner.mock.calls[0][0]).toBe("ot-1");
    expect(updateMandateSigner.mock.calls[0][1]).toBe("ms-1");
  });

  it("AC5: sin banderas de edición no hay botón de editar", async () => {
    fetchMandateSigners.mockResolvedValue([signer({ puedeEditar: false, puedeEliminar: false })]);
    renderSection();
    const tabla = await screen.findByRole("table", { name: "Mandatarios del organismo" });
    const fila = within(tabla).getByText("Hugo Mandatario").closest("tr") as HTMLElement;
    expect(within(fila).queryByRole("button", { name: /editar mandatario/i })).not.toBeInTheDocument();
  });

  it("AC4: la compañía sin mandatario activo en el organismo se marca «Sin mandatario»", async () => {
    listCompanyOtMandateRules.mockResolvedValue([
      companyRow({ companyTenantId: "cia-1", companyName: "Sin Nadie S.A.S." }),
      companyRow({ companyTenantId: "cia-2", companyName: "Con Default S.A.S.", defaultMandateSignerName: "Carlos Pérez" }),
      companyRow({ companyTenantId: "cia-3", companyName: "Con Vinculado S.A.S." }),
    ]);
    fetchMandateSigners.mockResolvedValue([signer({ companyTenantIds: ["cia-3"] })]);
    renderSection();
    const tabla = await screen.findByRole("table", { name: /empresas que radican/i });
    const fila = (n: string) => within(tabla).getByText(n).closest("tr") as HTMLElement;
    expect(within(fila("Sin Nadie S.A.S.")).getByTestId("ot-mandatos-sin-mandatario")).toHaveTextContent(
      "Sin mandatario",
    );
    expect(within(fila("Con Default S.A.S.")).queryByTestId("ot-mandatos-sin-mandatario")).not.toBeInTheDocument();
    expect(within(fila("Con Vinculado S.A.S.")).queryByTestId("ot-mandatos-sin-mandatario")).not.toBeInTheDocument();
  });

  it("AC3: los cuatro estados del hub se ven en verde, naranja, rojo y rojo con su texto", async () => {
    fetchMandateSigners.mockResolvedValue([
      signer({ id: "a", fullName: "Ana Vigente", documentNumber: "1" }),
      signer({ id: "b", fullName: "Beto Porvencer", documentNumber: "2", validityStatus: "por_vencer" }),
      signer({ id: "c", fullName: "Carla Vencida", documentNumber: "3", validityStatus: "vencido" }),
      signer({ id: "d", fullName: "Dario Inactivo", documentNumber: "4", validityStatus: "inactivo", isActive: false }),
    ]);
    renderSection();
    const tabla = await screen.findByRole("table", { name: "Mandatarios del organismo" });
    const color = (n: string) =>
      (within(within(tabla).getByText(n).closest("tr") as HTMLElement).getByRole("status") as HTMLElement).style.color;
    expect(["Ana Vigente", "Beto Porvencer", "Carla Vencida", "Dario Inactivo"].map(color)).toEqual([
      "var(--badge-success-fg)",
      "var(--badge-warning-fg)",
      "var(--badge-danger-fg)",
      "var(--badge-danger-fg)",
    ]);
  });
});

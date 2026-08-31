import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";
import { ToastProvider } from "@/components/admin/Toast";
import type { CompanyOtMandateRuleView } from "@/lib/api/admin-plataforma-mandatos";

const fetchMandateOtConfig = vi.fn();
const listCompanyOtMandateRules = vi.fn();
const fetchCompanyTransitOffices = vi.fn();
const fetchRepresentedCompanies = vi.fn();
const createCompanyMandateSigner = vi.fn();
const fetchMandateSigners = vi.fn();
const fetchMandateSignerSignatureImage = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  fetchMandateOtConfig: (...a: unknown[]) => fetchMandateOtConfig(...a),
  listCompanyOtMandateRules: (...a: unknown[]) => listCompanyOtMandateRules(...a),
  upsertMandateOtConfig: vi.fn(),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
  fetchMandateOtPreview: vi.fn(),
  fetchMandatoTemplatePreview: vi.fn(),
  fetchMandateSigners: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
  fetchMandateSignerSignatureImage: (...a: unknown[]) => fetchMandateSignerSignatureImage(...a),
  fetchCompanyTransitOffices: (...a: unknown[]) => fetchCompanyTransitOffices(...a),
  fetchRepresentedCompanies: (...a: unknown[]) => fetchRepresentedCompanies(...a),
  createCompanyMandateSigner: (...a: unknown[]) => createCompanyMandateSigner(...a),
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

describe("OtMandatosSection", () => {
  beforeEach(() => {
    fetchMandateOtConfig.mockReset();
    listCompanyOtMandateRules.mockReset();
    fetchMandateSigners.mockReset();
    fetchMandateSignerSignatureImage.mockReset();
    listCompanyOtMandateRules.mockResolvedValue([]);
    fetchMandateSigners.mockResolvedValue([]);
    fetchMandateSignerSignatureImage.mockResolvedValue(new Blob(["png"], { type: "image/png" }));
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:firma-mock");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    fetchCompanyTransitOffices.mockResolvedValue([
      { transitOfficeId: "ot-1", code: "11001000", name: "OT Bogotá" },
    ]);
    fetchRepresentedCompanies.mockResolvedValue([]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("lista empresas habilitadas y permite editar el mandatario", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([
      companyRow({
        defaultMandateSignerName: "Carlos Pérez",
        defaultMandateSignerDocumentType: "CC",
        defaultMandateSignerDocumentNumber: "1020304050",
        defaultMandateSignerIntegrityHash: "a".repeat(64),
      }),
    ]);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ot-mandatos-company-table")).toBeInTheDocument();
    expect(screen.getByText("Gestora de Prueba S.A.S.")).toBeInTheDocument();
    expect(screen.getByText("900123456")).toBeInTheDocument();
    expect(screen.queryByText("CIA-1")).not.toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: /^código$/i })).not.toBeInTheDocument();
    expect(screen.getByText("Carlos Pérez")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /editar mandatario de gestora de prueba/i })).toBeInTheDocument();
  });

  it("muestra Sin definir cuando el OT no tiene mandatario general", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ot-mandatos-general-signer")).toHaveTextContent("Sin definir");
  });

  it("ofrece registrar el mandatario default desde Configurar mandato del OT", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow()]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await user.click(await screen.findByRole("button", { name: /editar mandatario general del organismo/i }));
    expect(await screen.findByTestId("mandato-ot-register-signer")).toBeInTheDocument();
    await user.click(screen.getByTestId("mandato-ot-register-signer"));
    expect(await screen.findByRole("dialog", { name: /registrar mandatario/i })).toBeInTheDocument();
  });

  it("abre la configuración de mandatario de la empresa", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([companyRow({ assignmentMode: "signer", hasExplicitRule: true })]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await user.click(await screen.findByRole("button", { name: /editar mandatario de gestora de prueba/i }));
    expect(await screen.findByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandatario");
  });

  it("al cerrar el panel de empresa recarga la grilla", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockImplementation(() => {
      const call = listCompanyOtMandateRules.mock.calls.length;
      const name = call >= 3 ? "Ana López" : "Carlos Pérez";
      return Promise.resolve([companyRow({ defaultMandateSignerName: name })]);
    });
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByText("Carlos Pérez")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /editar mandatario de gestora de prueba/i }));
    const closeButtons = await screen.findAllByRole("button", { name: /^cerrar$/i });
    const footerClose = closeButtons.find((el) => el.textContent?.trim() === "Cerrar");
    expect(footerClose).toBeDefined();
    await user.click(footerClose!);
    expect(await screen.findByText("Ana López")).toBeInTheDocument();
    expect(listCompanyOtMandateRules.mock.calls.length).toBeGreaterThanOrEqual(3);
  });

  it("muestra vacío cuando no hay empresas con el OT habilitado", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(
      await screen.findByText(/no hay empresas con este organismo habilitado/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/no hay mandatarios creados en este organismo/i)).toBeInTheDocument();
  });

  it("lista mandatarios creados con tipo de firma y permite verla", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    fetchMandateSigners.mockResolvedValue([
      {
        id: "ms-1",
        transitOfficeId: "ot-1",
        fullName: "Hugo Mandatario",
        documentType: "CC",
        documentNumber: "52123456",
        integrityHash: "h".repeat(64),
        email: "hugo@ot.test",
        userId: null,
        identityValidationRef: null,
        identityStatus: "none",
        signatureVaultId: "vault-1",
        registeredAt: "2026-01-01T00:00:00Z",
        isActive: true,
        companyTenantIds: [],
        physicalSignatureOfficeIds: [],
      },
    ]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByRole("table", { name: "Mandatarios del organismo" })).toBeInTheDocument();
    expect(screen.getByText("Hugo Mandatario")).toBeInTheDocument();
    expect(screen.getByText("CC")).toBeInTheDocument();
    expect(screen.getByText("52123456")).toBeInTheDocument();
    expect(screen.getByText("Firma del baúl")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /ver firma de hugo mandatario/i }));
    expect(await screen.findByRole("dialog", { name: /firma de hugo mandatario/i })).toBeInTheDocument();
    expect(await screen.findByRole("img", { name: /firma de hugo mandatario/i })).toBeInTheDocument();
    expect(fetchMandateSignerSignatureImage).toHaveBeenCalledWith("ot-1", "ms-1");
  });

  it("muestra error con reintentar", async () => {
    fetchMandateOtConfig.mockRejectedValue(new Error("boom"));
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    await waitFor(() => expect(fetchMandateOtConfig).toHaveBeenCalledTimes(2));
  });
});

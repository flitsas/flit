import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";
import { ToastProvider } from "@/components/admin/Toast";

const fetchMandateOtConfig = vi.fn();
const listCompanyOtMandateRules = vi.fn();
const fetchCompanyTransitOffices = vi.fn();
const fetchRepresentedCompanies = vi.fn();
const createCompanyMandateSigner = vi.fn();

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
  fetchMandateSigners: vi.fn().mockResolvedValue([]),
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
};

describe("OtMandatosSection", () => {
  beforeEach(() => {
    fetchMandateOtConfig.mockReset();
    listCompanyOtMandateRules.mockReset();
    listCompanyOtMandateRules.mockResolvedValue([]);
    fetchCompanyTransitOffices.mockResolvedValue([
      { transitOfficeId: "ot-1", code: "11001000", name: "OT Bogotá" },
    ]);
    fetchRepresentedCompanies.mockResolvedValue([]);
  });

  it("lista empresas habilitadas con CTA para registrar mandato", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([
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
      },
    ]);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ot-mandatos-company-list")).toBeInTheDocument();
    expect(screen.getByText("Gestora de Prueba S.A.S.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /registrar mandato/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /tipo por empresa que radica/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /registrar mandatario/i })).toBeInTheDocument();
  });

  it("abre el formulario de mandatario desde la tarjeta de la empresa", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    listCompanyOtMandateRules.mockResolvedValue([
      {
        companyTenantId: "cia-1",
        companyName: "Gestora de Prueba S.A.S.",
        assignmentMode: "signer",
        mandataryFamily: "individuo",
        institutionalMandataryName: null,
        institutionalMandataryNit: null,
        chamberCity: null,
        mandatarySigla: null,
        hasExplicitRule: true,
        defaultMandateSignerId: null,
      },
    ]);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    await user.click(await screen.findByRole("button", { name: /registrar mandatario/i }));
    expect(await screen.findByRole("dialog", { name: /registrar mandatario/i })).toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: /registrar mandatario/i }).className).toMatch(
      /z-\[80\]/,
    );
    expect(screen.getByLabelText(/nombre completo/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/tipo de documento/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/número de documento/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^correo$/i)).toBeInTheDocument();
  });

  it("muestra vacío cuando no hay empresas con el OT habilitado", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(await screen.findByTestId("ui-empty")).toBeInTheDocument();
    expect(screen.getByText(/no hay empresas con este organismo habilitado/i)).toBeInTheDocument();
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

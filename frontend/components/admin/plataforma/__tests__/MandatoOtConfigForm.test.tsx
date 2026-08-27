import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatoOtConfigForm } from "@/components/admin/plataforma/MandatoOtConfigForm";
import type { MandateOtConfigView } from "@/lib/api/admin-plataforma-mandatos";

const listCompanyOtMandateRules = vi.fn();
const fetchMandateSigners = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-plataforma-mandatos")>();
  return {
    ...actual,
    listCompanyOtMandateRules: (...a: unknown[]) => listCompanyOtMandateRules(...a),
    upsertMandateOtConfig: vi.fn(),
    fetchMandateOtPreview: vi.fn(),
    fetchMandatoTemplatePreview: vi.fn(),
    uploadMandateOtPdfTemplate: vi.fn(),
    saveMandateOtEditorBody: vi.fn(),
    deleteMandateOtCustomTemplate: vi.fn(),
    upsertCompanyOtMandateRule: vi.fn(),
    deleteCompanyOtMandateRule: vi.fn(),
  };
});

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: (...a: unknown[]) => fetchMandateSigners(...a),
}));

const funza = {
  officeId: "eeacc872-a522-56bb-9150-70776b094009",
  code: "25286000",
  name: "STRIA TTOyTTE MCPAL FUNZA",
  templateCode: "municipio",
  configuredTemplateCode: "municipio",
  mandataryFamily: "individuo",
  requiresForNaturalPerson: true,
  hasExplicitConfig: true,
  assignmentMode: "open",
  customTemplateKind: "none",
  hasCustomTemplate: false,
  defaultMandateSignerId: null,
  rowVersion: 1,
} as MandateOtConfigView;

describe("MandatoOtConfigForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    listCompanyOtMandateRules.mockResolvedValue([]);
    fetchMandateSigners.mockResolvedValue([]);
  });

  it("abre Configurar mandato de Funza sin ReferenceError (error state)", async () => {
    render(
      <MandatoOtConfigForm
        office={funza}
        mode="mandato"
        onClose={() => undefined}
        onSaved={() => undefined}
      />,
    );

    await waitFor(() => {
      expect(screen.getByTestId("mandato-ot-config-form")).toBeInTheDocument();
    });
    expect(screen.getByTestId("mandato-template-select")).toBeInTheDocument();
    expect(listCompanyOtMandateRules).toHaveBeenCalledWith(funza.officeId);
    expect(screen.queryByTestId("mandato-ot-register-signer")).not.toBeInTheDocument();
    expect(screen.getByText(/el default cliente×ot prima sobre este/i)).toBeInTheDocument();
  });

  it("permite registrar el mandatario default del OT desde el panel", async () => {
    const onRegisterSigner = vi.fn();
    listCompanyOtMandateRules.mockResolvedValue([
      {
        companyTenantId: "cia-1",
        companyName: "Gestora Funza S.A.S.",
        assignmentMode: "signer",
        hasExplicitRule: true,
        defaultMandateSignerId: null,
      },
    ]);

    render(
      <MandatoOtConfigForm
        office={funza}
        mode="mandato"
        onRegisterSigner={onRegisterSigner}
        onClose={() => undefined}
        onSaved={() => undefined}
      />,
    );

    const cta = await screen.findByTestId("mandato-ot-register-signer");
    cta.click();
    expect(onRegisterSigner).toHaveBeenCalledWith("cia-1");
  });
});

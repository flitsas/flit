import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";
import { ToastProvider } from "@/components/admin/Toast";

const fetchMandateOtConfig = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  fetchMandateOtConfig: (...a: unknown[]) => fetchMandateOtConfig(...a),
  listCompanyOtMandateRules: vi.fn().mockResolvedValue([]),
  upsertMandateOtConfig: vi.fn(),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
  fetchMandateOtPreview: vi.fn(),
  fetchMandatoTemplatePreview: vi.fn(),
  fetchMandateSigners: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: vi.fn().mockResolvedValue([]),
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
  });

  it("muestra carga y luego el modelo abierto del OT", async () => {
    fetchMandateOtConfig.mockResolvedValue(office);
    render(
      <ToastProvider>
        <OtMandatosSection transitOfficeId="ot-1" />
      </ToastProvider>,
    );
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
    expect(await screen.findByTestId("ot-mandatos-section")).toBeInTheDocument();
    expect(screen.getByText(/formato abierto|abierto/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /configurar mandato/i })).toBeInTheDocument();
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

import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatosCatalogPanel } from "@/components/admin/plataforma/MandatosCatalogPanel";

const listMandateOtConfigs = vi.fn();
const deleteMandateOtConfig = vi.fn();
const openPdfBlobInNewTab = vi.fn();
const fetchMandatoTemplatePreview = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  listMandateOtConfigs: (...a: unknown[]) => listMandateOtConfigs(...a),
  deleteMandateOtConfig: (...a: unknown[]) => deleteMandateOtConfig(...a),
  fetchMandatoTemplatePreview: (...a: unknown[]) => fetchMandatoTemplatePreview(...a),
  fetchMandateOtPreview: vi.fn(),
  upsertMandateOtConfig: vi.fn(),
  extractMandateConfigFromFile: vi.fn(),
  uploadMandateOtPdfTemplate: vi.fn(),
  saveMandateOtEditorBody: vi.fn(),
  deleteMandateOtCustomTemplate: vi.fn(),
  listCompanyOtMandateRules: vi.fn().mockResolvedValue([]),
  upsertCompanyOtMandateRule: vi.fn(),
  deleteCompanyOtMandateRule: vi.fn(),
}));

vi.mock("@/lib/documents/open-document-tab", () => ({
  openPdfBlobInNewTab: (...a: unknown[]) => openPdfBlobInNewTab(...a),
}));

const sampleRows = [
  {
    officeId: "o1",
    code: "5631000",
    name: "Sabaneta",
    templateCode: "sabaneta",
    requiresForNaturalPerson: true,
    mandataryFamily: "organismo_transito",
    assignmentMode: "institutional",
    institutionalMandataryName: "UT-SETSA",
    institutionalMandataryNit: "900273813-7",
    chamberCity: "Medellín",
    mandatarySigla: "UT-SETSA",
    hasExplicitConfig: true,
    rowVersion: 1,
    customTemplateKind: "none",
    customTemplateFileName: null,
    customTemplateBody: null,
    hasCustomTemplate: false,
  },
  {
    officeId: "o2",
    code: "05001000",
    name: "Medellín",
    templateCode: "generico",
    requiresForNaturalPerson: false,
    mandataryFamily: "individuo",
    assignmentMode: "signer",
    institutionalMandataryName: null,
    institutionalMandataryNit: null,
    chamberCity: null,
    mandatarySigla: null,
    hasExplicitConfig: false,
    rowVersion: null,
    customTemplateKind: "none",
    customTemplateFileName: null,
    customTemplateBody: null,
    hasCustomTemplate: false,
  },
];

describe("MandatosCatalogPanel configurador", () => {
  beforeEach(() => {
    listMandateOtConfigs.mockReset();
    deleteMandateOtConfig.mockReset();
    openPdfBlobInNewTab.mockReset();
    fetchMandatoTemplatePreview.mockReset();
    listMandateOtConfigs.mockResolvedValue(sampleRows);
    openPdfBlobInNewTab.mockResolvedValue(undefined);
    fetchMandatoTemplatePreview.mockResolvedValue(new Blob(["%PDF"], { type: "application/pdf" }));
  });

  it("carga OTs desde la API y muestra Configurar", async () => {
    render(<MandatosCatalogPanel />);
    expect(await screen.findByText("Sabaneta")).toBeInTheDocument();
    expect(screen.getByText("Medellín")).toBeInTheDocument();
    expect(screen.getAllByText("Por compañía").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /configurar mandato de sabaneta/i })).toBeInTheDocument();
  });

  it("abre el formulario al configurar", async () => {
    const user = userEvent.setup();
    render(<MandatosCatalogPanel />);
    await screen.findByText("Sabaneta");
    await user.click(screen.getByRole("button", { name: /configurar mandato de sabaneta/i }));
    expect(screen.getByTestId("mandato-ot-config-form")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /configurar mandato/i })).toBeInTheDocument();
  });

  it("restablece default con DELETE", async () => {
    const user = userEvent.setup();
    deleteMandateOtConfig.mockResolvedValue(undefined);
    listMandateOtConfigs
      .mockResolvedValueOnce(sampleRows)
      .mockResolvedValueOnce([
        { ...sampleRows[0], hasExplicitConfig: false, templateCode: "generico", rowVersion: null },
        sampleRows[1],
      ]);

    render(<MandatosCatalogPanel />);
    await screen.findByText("Sabaneta");
    await user.click(screen.getByRole("button", { name: /restablecer default de sabaneta/i }));
    await waitFor(() => expect(deleteMandateOtConfig).toHaveBeenCalledWith("o1"));
  });

  it("abre preview de plantilla genérica", async () => {
    const user = userEvent.setup();
    render(<MandatosCatalogPanel />);
    await screen.findByText("Sabaneta");
    const card = screen.getByTestId("mandato-template-generico");
    await user.click(within(card).getByRole("button", { name: /ver documento de mandato generico/i }));
    await waitFor(() => expect(openPdfBlobInNewTab).toHaveBeenCalled());
  });
});

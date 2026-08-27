import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatosCatalogPanel } from "@/components/admin/plataforma/MandatosCatalogPanel";
import { ToastProvider } from "@/components/admin/Toast";

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
  // El panel monta el simulador (HU #11707); sin estas entradas el módulo mockeado las deja
  // indefinidas y el componente revienta al montar.
  listMandateSimulatorSigners: vi.fn().mockResolvedValue([]),
  fetchMandateSimulationPreview: vi.fn(),
  sendMandateSimulation: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

vi.mock("@/lib/documents/open-document-tab", () => ({
  openPdfBlobInNewTab: (...a: unknown[]) => openPdfBlobInNewTab(...a),
}));

function renderPanel() {
  return render(
    <ToastProvider>
      <MandatosCatalogPanel />
    </ToastProvider>,
  );
}

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
    defaultMandateSignerId: null,
    defaultMandateSignerName: null,
    defaultMandateSignerDocumentType: null,
    defaultMandateSignerDocumentNumber: null,
    defaultMandateSignerIntegrityHash: null,
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
    defaultMandateSignerId: null,
    defaultMandateSignerName: null,
    defaultMandateSignerDocumentType: null,
    defaultMandateSignerDocumentNumber: null,
    defaultMandateSignerIntegrityHash: null,
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

  it("carga OTs desde la API y muestra Acciones", async () => {
    renderPanel();
    expect(await screen.findByText("Sabaneta")).toBeInTheDocument();
    expect(screen.getByText("Medellín")).toBeInTheDocument();
    expect(screen.getAllByText("Por compañía").length).toBeGreaterThan(0);
    expect(
      screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }),
    ).toBeInTheDocument();
  });

  it("abre configuración del mandato desde el menú Acciones", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByText("Sabaneta");
    await user.click(screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }));
    await user.click(screen.getByRole("menuitem", { name: /configuración del mandato/i }));
    expect(screen.getByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandato");
    expect(screen.getByRole("heading", { name: /configurar mandato/i })).toBeInTheDocument();
  });

  it("abre configuración del mandatario desde el menú Acciones", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByText("Sabaneta");
    await user.click(screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }));
    await user.click(screen.getByRole("menuitem", { name: /configuración del mandatario/i }));
    expect(screen.getByTestId("mandato-ot-config-form")).toHaveAttribute("data-mode", "mandatario");
    expect(screen.getByRole("heading", { name: /configurar mandatario/i })).toBeInTheDocument();
  });

  it("restablece default con DELETE desde Acciones", async () => {
    const user = userEvent.setup();
    deleteMandateOtConfig.mockResolvedValue(undefined);
    listMandateOtConfigs
      .mockResolvedValueOnce(sampleRows)
      .mockResolvedValueOnce([
        { ...sampleRows[0], hasExplicitConfig: false, templateCode: "generico", rowVersion: null },
        sampleRows[1],
      ]);

    renderPanel();
    await screen.findByText("Sabaneta");
    await user.click(screen.getByRole("button", { name: /acciones de mandato para sabaneta/i }));
    await user.click(screen.getByRole("menuitem", { name: /restablecer default/i }));
    await waitFor(() => expect(deleteMandateOtConfig).toHaveBeenCalledWith("o1"));
  });

  it("abre preview de plantilla genérica", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByText("Sabaneta");
    const card = screen.getByTestId("mandato-template-generico");
    await user.click(within(card).getByRole("button", { name: /ver documento de mandato generico/i }));
    await waitFor(() => expect(openPdfBlobInNewTab).toHaveBeenCalled());
  });

  it("pagina la tabla de organismos (DataTable + Pagination)", async () => {
    const user = userEvent.setup();
    const many = Array.from({ length: 12 }, (_, i) => ({
      ...sampleRows[1],
      officeId: `office-${i}`,
      code: `500100${i}`,
      name: `Organismo ${i + 1}`,
    }));
    listMandateOtConfigs.mockResolvedValue(many);

    renderPanel();
    expect(await screen.findByText("Organismo 1")).toBeInTheDocument();
    expect(screen.getByText("Organismo 10")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 11")).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: /paginación/i })).toHaveTextContent(
      /Mostrando 1–10 de 12/i,
    );

    await user.click(screen.getByRole("button", { name: /página siguiente/i }));
    expect(await screen.findByText("Organismo 11")).toBeInTheDocument();
    expect(screen.getByText("Organismo 12")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 1")).not.toBeInTheDocument();
  });
});

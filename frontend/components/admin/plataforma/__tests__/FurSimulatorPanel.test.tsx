import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { FurSimulatorPanel } from "@/components/admin/plataforma/FurSimulatorPanel";
import { ToastProvider } from "@/components/admin/Toast";

const listProcedureTypes = vi.fn();
const fetchFurPreview = vi.fn();
const listFurClassifications = vi.fn();
const openPdfBlobInNewTab = vi.fn();

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listProcedureTypes: (...a: unknown[]) => listProcedureTypes(...a),
  },
}));

vi.mock("@/lib/api/admin-plataforma-fur", () => ({
  fetchFurPreview: (...a: unknown[]) => fetchFurPreview(...a),
  listFurClassifications: (...a: unknown[]) => listFurClassifications(...a),
}));

vi.mock("@/lib/documents/open-document-tab", () => ({
  openPdfBlobInNewTab: (...a: unknown[]) => openPdfBlobInNewTab(...a),
}));

function renderPanel() {
  return render(
    <ToastProvider>
      <FurSimulatorPanel />
    </ToastProvider>,
  );
}

const sampleTypes = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    code: "MATRICULA_NUEVA",
    name: "Matrícula inicial",
    family: "MATRICULAS",
    publicationStatus: "published",
    isActive: true,
    publishedAt: null,
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    code: "TRASPASO_STANDARD",
    name: "Traspaso",
    family: "TRASPASO",
    publicationStatus: "published",
    isActive: true,
    publishedAt: null,
  },
  {
    id: "33333333-3333-3333-3333-333333333333",
    code: "CAMBIO_COLOR",
    name: "Cambio de color",
    family: "OTROS",
    publicationStatus: "published",
    isActive: true,
    publishedAt: null,
  },
];

const sampleClassifications = [
  { classification: "AUTOMOVIL", templateFormat: "AUTOMOTOR", fieldToFill: "AUTOMOVIL" },
  { classification: "EXCAVADORA", templateFormat: "MAQUINARIA", fieldToFill: "CONSTRUCCION" },
  { classification: "REMOLQUE", templateFormat: "REMOLQUES", fieldToFill: "REMOLQUE" },
];

describe("FurSimulatorPanel", () => {
  beforeEach(() => {
    listProcedureTypes.mockReset();
    fetchFurPreview.mockReset();
    listFurClassifications.mockReset();
    openPdfBlobInNewTab.mockReset();
    listProcedureTypes.mockResolvedValue(sampleTypes);
    listFurClassifications.mockResolvedValue(sampleClassifications);
    fetchFurPreview.mockResolvedValue(new Blob(["%PDF"], { type: "application/pdf" }));
    openPdfBlobInNewTab.mockImplementation(async (fn: () => Promise<Blob>) => {
      await fn();
    });
  });

  it("deshabilita Simular FUR hasta familia, tipo y vehículo", async () => {
    renderPanel();
    const submit = await screen.findByRole("button", { name: /simular fur/i });
    expect(submit).toBeDisabled();
    const fillAll = screen.getByRole("button", { name: /simular llenado completo — automotor/i });
    expect(fillAll).toBeEnabled();
  });

  it("simula llenado completo de automotor sin tipo ni clase y envía fillAll + templateFormat", async () => {
    const user = userEvent.setup();
    renderPanel();
    const fillAll = await screen.findByRole("button", { name: /simular llenado completo — automotor/i });
    await user.click(fillAll);
    await waitFor(() => expect(openPdfBlobInNewTab).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(fetchFurPreview).toHaveBeenCalledTimes(1));
    expect(fetchFurPreview).toHaveBeenCalledWith({ fillAll: true, templateFormat: "AUTOMOTOR" });
  });

  it("llenado completo de maquinaria no depende de la clase seleccionada", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByLabelText(/tipo de clase del vehículo/i);
    await user.selectOptions(screen.getByLabelText(/tipo de clase del vehículo/i), "EXCAVADORA");
    await user.click(screen.getByRole("button", { name: /simular llenado completo — maquinaria/i }));
    await waitFor(() =>
      expect(fetchFurPreview).toHaveBeenCalledWith({ fillAll: true, templateFormat: "MAQUINARIA" }),
    );
  });

  it("deshabilita vendedor en matrícula y muestra la pista", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByLabelText(/tipo de trámite padre/i);
    await user.selectOptions(screen.getByLabelText(/tipo de trámite padre/i), "MATRICULAS");
    await user.selectOptions(screen.getByLabelText(/^tipo de trámite$/i), sampleTypes[0].id);
    expect(screen.getByLabelText(/^vendedor$/i)).toBeDisabled();
    expect(screen.getByTestId("fur-seller-hint")).toBeInTheDocument();
  });

  it("habilita vendedor en traspaso y abre el PDF en ventana nueva con transformaciones", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByLabelText(/tipo de trámite padre/i);
    await user.selectOptions(screen.getByLabelText(/tipo de trámite padre/i), "TRASPASO");
    await user.selectOptions(screen.getByLabelText(/^tipo de trámite$/i), sampleTypes[1].id);
    await user.selectOptions(screen.getByLabelText(/tipo de clase del vehículo/i), "AUTOMOVIL");
    expect(screen.getByLabelText(/^vendedor$/i)).toBeEnabled();

    await user.click(screen.getByLabelText(/cambio de color/i));
    await user.selectOptions(screen.getByLabelText(/^prenda$/i), "inscripcion");

    await user.click(screen.getByRole("button", { name: /simular fur/i }));
    await waitFor(() => expect(openPdfBlobInNewTab).toHaveBeenCalledTimes(1));
    expect(openPdfBlobInNewTab).toHaveBeenCalledWith(expect.any(Function), { maximize: true });
    await waitFor(() => expect(fetchFurPreview).toHaveBeenCalledTimes(1));
    expect(fetchFurPreview).toHaveBeenCalledWith(
      expect.objectContaining({
        procedureTypeId: sampleTypes[1].id,
        vehicleClass: "AUTOMOVIL",
        sellerPersonKind: "natural",
        buyerPersonKind: "natural",
        cambioColor: true,
        cambioCombustible: false,
        cambioCarroceria: false,
        blindaje: false,
        prenda: "inscripcion",
      }),
    );
  });

  it("deshabilita cambio de color cuando el tipo base ya es esa capa", async () => {
    const user = userEvent.setup();
    renderPanel();
    await screen.findByLabelText(/tipo de trámite padre/i);
    await user.selectOptions(screen.getByLabelText(/tipo de trámite padre/i), "OTROS");
    await user.selectOptions(screen.getByLabelText(/^tipo de trámite$/i), sampleTypes[2].id);
    expect(screen.getByLabelText(/cambio de color/i)).toBeDisabled();
    expect(screen.getByLabelText(/cambio de color/i)).toBeChecked();
    expect(screen.getByText(/casillas = tipo \+ prenda \+ transformaciones/i)).toBeInTheDocument();
    expect(screen.getByTestId("fur-result-guide")).toBeInTheDocument();
    expect(screen.getByText(/párrafo 23/i)).toBeInTheDocument();
  });
});

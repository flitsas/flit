import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MandatoSimuladorPanel } from "@/components/admin/plataforma/MandatoSimuladorPanel";
import { ToastProvider } from "@/components/admin/Toast";
import type { MandateOtConfigView } from "@/lib/api/admin-plataforma-mandatos";

const listMandateSimulatorSigners = vi.fn();
const fetchMandateSimulationPreview = vi.fn();
const sendMandateSimulation = vi.fn();
const openPdfBlobInNewTab = vi.fn();
const listPublishedProcedureTypes = vi.fn();

vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  listMandateSimulatorSigners: (...a: unknown[]) => listMandateSimulatorSigners(...a),
  fetchMandateSimulationPreview: (...a: unknown[]) => fetchMandateSimulationPreview(...a),
  sendMandateSimulation: (...a: unknown[]) => sendMandateSimulation(...a),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: (...a: unknown[]) => listPublishedProcedureTypes(...a),
  },
}));

vi.mock("@/lib/documents/open-document-tab", () => ({
  // El helper real recibe un thunk y lo ejecuta; el mock hace lo mismo para que la llamada al
  // backend ocurra de verdad y se pueda afirmar sobre el escenario enviado.
  openPdfBlobInNewTab: async (thunk: () => Promise<Blob>) => {
    const blob = await thunk();
    openPdfBlobInNewTab(blob);
  },
}));

const funza = {
  officeId: "ot-funza",
  code: "25286000",
  name: "STRIA TTOyTTE MCPAL FUNZA",
  templateCode: "municipio",
  configuredTemplateCode: "municipio",
} as unknown as MandateOtConfigView;

function renderPanel() {
  return render(
    <ToastProvider>
      <MandatoSimuladorPanel offices={[funza]} />
    </ToastProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  listPublishedProcedureTypes.mockResolvedValue([
    {
      id: "pt-mat",
      code: "MATRICULA_NUEVA",
      name: "Matrícula inicial",
      family: "MATRICULAS",
      publicationStatus: "published",
      isActive: true,
      publishedAt: null,
    },
    {
      id: "pt-tra",
      code: "TRASPASO_STANDARD",
      name: "Traspaso",
      family: "TRASPASO",
      publicationStatus: "published",
      isActive: true,
      publishedAt: null,
    },
  ]);
  listMandateSimulatorSigners.mockResolvedValue([
    {
      id: "signer-1",
      fullName: "Ana Gestora",
      documentNumber: "1020304050",
      identityVigente: true,
      tieneFirmaEnBaul: true,
    },
  ]);
});

// Uso de ejemplo:
// <MandatoSimuladorPanel offices={rows} /> → escenario + "Ver PDF" / "Enviar por correo"

describe("MandatoSimuladorPanel — armado del escenario (HU #11707)", () => {
  it("AC3: al elegir organismo carga solo los mandatarios habilitados de ese OT", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");

    await waitFor(() => expect(listMandateSimulatorSigners).toHaveBeenCalledWith("ot-funza"));
    expect(await screen.findByRole("option", { name: /Ana Gestora/ })).toBeInTheDocument();
  });

  it("el tipo de trámite muestra el nombre y el code del catálogo", async () => {
    renderPanel();

    expect(await screen.findByRole("option", { name: /Traspaso \(TRASPASO_STANDARD\)/ })).toBeInTheDocument();
  });

  it("AC5: la vista previa pide el PDF con el escenario armado y lo abre", async () => {
    const user = userEvent.setup();
    const blob = new Blob(["%PDF-1.4"], { type: "application/pdf" });
    fetchMandateSimulationPreview.mockResolvedValue(blob);
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");
    await screen.findByRole("option", { name: /Ana Gestora/ });
    await user.selectOptions(screen.getByTestId("simulador-persona"), "natural");
    await user.selectOptions(screen.getByTestId("simulador-familia"), "MATRICULAS");
    await waitFor(() =>
      expect(screen.getByTestId("simulador-tramite")).toHaveValue("MATRICULA_NUEVA"),
    );
    await user.selectOptions(screen.getByTestId("simulador-mandatario"), "signer-1");
    await user.click(screen.getByRole("button", { name: /Ver PDF/ }));

    await waitFor(() =>
      expect(fetchMandateSimulationPreview).toHaveBeenCalledWith({
        officeId: "ot-funza",
        personType: "natural",
        procedureTypeCode: "MATRICULA_NUEVA",
        assignmentMode: null,
        mandateSignerId: "signer-1",
        prenda: "ninguna",
        cambioColor: false,
        cambioCombustible: false,
        cambioCarroceria: false,
        blindaje: false,
      }),
    );
    expect(openPdfBlobInNewTab).toHaveBeenCalledWith(blob);
  });

  it("por defecto simula un traspaso con persona jurídica", async () => {
    const user = userEvent.setup();
    fetchMandateSimulationPreview.mockResolvedValue(new Blob(["%PDF-1.4"], { type: "application/pdf" }));
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");
    await waitFor(() =>
      expect(screen.getByTestId("simulador-tramite")).toHaveValue("TRASPASO_STANDARD"),
    );
    await user.click(screen.getByRole("button", { name: /Ver PDF/ }));

    await waitFor(() =>
      expect(fetchMandateSimulationPreview).toHaveBeenCalledWith(
        expect.objectContaining({ personType: "juridica", procedureTypeCode: "TRASPASO_STANDARD" }),
      ),
    );
  });

  it("AC4: un organismo sin mandatarios habilitados se avisa y deja simular igual", async () => {
    listMandateSimulatorSigners.mockResolvedValue([]);
    const user = userEvent.setup();
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");

    expect(await screen.findByTestId("simulador-sin-mandatarios")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByTestId("simulador-tramite")).toHaveValue("TRASPASO_STANDARD"),
    );
    expect(screen.getByRole("button", { name: /Ver PDF/ })).toBeEnabled();
  });

  it("el envío por correo NO se ofrece: el simulador solo previsualiza", async () => {
    // Decisión de producto del 2026-08-21: la función se ocultó, no se eliminó.
    const user = userEvent.setup();
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");

    expect(screen.queryByTestId("simulador-correo")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Enviar por correo/ })).not.toBeInTheDocument();
    expect(sendMandateSimulation).not.toHaveBeenCalled();
  });

  it("un fallo del backend se muestra en lenguaje de negocio", async () => {
    const { ApiError } = await import("@/lib/api/types");
    fetchMandateSimulationPreview.mockRejectedValue(
      new ApiError(404, "fallo", { message: "El mandatario indicado no está habilitado para ese organismo." }),
    );
    const user = userEvent.setup();
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");
    await waitFor(() =>
      expect(screen.getByTestId("simulador-tramite")).toHaveValue("TRASPASO_STANDARD"),
    );
    await user.click(screen.getByRole("button", { name: /Ver PDF/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "El mandatario indicado no está habilitado para ese organismo.",
    );
  });

  it("edge case — en modo institucional el mandatario no aplica y queda deshabilitado", async () => {
    const user = userEvent.setup();
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");
    await screen.findByRole("option", { name: /Ana Gestora/ });
    await user.selectOptions(screen.getByTestId("simulador-tipo"), "institucional");

    expect(screen.getByTestId("simulador-mandatario")).toBeDisabled();
  });

  it("envía prenda y transformaciones al generar el PDF", async () => {
    const user = userEvent.setup();
    fetchMandateSimulationPreview.mockResolvedValue(new Blob(["%PDF-1.4"], { type: "application/pdf" }));
    renderPanel();

    await user.selectOptions(screen.getByTestId("simulador-ot"), "ot-funza");
    await waitFor(() =>
      expect(screen.getByTestId("simulador-tramite")).toHaveValue("TRASPASO_STANDARD"),
    );
    await user.selectOptions(screen.getByTestId("simulador-prenda"), "inscripcion");
    await user.click(screen.getByTestId("simulador-cambio-color"));
    await user.click(screen.getByRole("button", { name: /Ver PDF/ }));

    await waitFor(() =>
      expect(fetchMandateSimulationPreview).toHaveBeenCalledWith(
        expect.objectContaining({
          prenda: "inscripcion",
          cambioColor: true,
          cambioCombustible: false,
          cambioCarroceria: false,
          blindaje: false,
        }),
      ),
    );
  });
});

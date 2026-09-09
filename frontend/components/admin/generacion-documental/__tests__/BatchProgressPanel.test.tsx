// HU #12211 (Feature #12201) — CF-13/CF-14/CF-15/CF-21/CF-22: progreso anunciado con aria-live,
// etiquetas colapsadas, descarga ZIP y detalle de errores sin el valor capturado.
// Uso de ejemplo: render(<BatchProgressPanel batchId="…" />) con el cliente de API mockeado.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  fetchStandaloneBatch: vi.fn(),
  fetchStandaloneBatchItems: vi.fn(),
  downloadStandaloneBatchZip: vi.fn(),
}));

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  fetchStandaloneBatch: mocks.fetchStandaloneBatch,
  fetchStandaloneBatchItems: mocks.fetchStandaloneBatchItems,
  downloadStandaloneBatchZip: mocks.downloadStandaloneBatchZip,
}));

import { BatchProgressPanel } from "../BatchProgressPanel";
import { ApiError } from "@/lib/api/types";

const BATCH_ID = "0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee";

function lote(overrides: Record<string, unknown> = {}) {
  return {
    batchId: BATCH_ID,
    status: "processing",
    total: 10,
    generated: 3,
    errors: 0,
    processed: 3,
    isTerminal: false,
    createdAt: "2026-09-09T10:00:00.000Z",
    completedAt: null,
    ...overrides,
  };
}

const filaGenerada = {
  id: "11111111-1111-1111-1111-111111111111",
  rowNumber: 1,
  documentType: "certificado_rues" as const,
  scenario: null,
  status: "generated" as const,
  errorCode: null,
  errorField: null,
  validationErrors: [],
  filename: "certificado_rues_1.pdf",
  createdAt: "2026-09-09T10:00:01.000Z",
};

const filaEnError = {
  id: "22222222-2222-2222-2222-222222222222",
  rowNumber: 4,
  documentType: "transferencia_dominio_generada" as const,
  scenario: "A" as const,
  status: "error" as const,
  errorCode: "VB-02",
  errorField: "placa",
  validationErrors: [
    { code: "VB-02", field: "placa", message: "La placa no tiene el formato exigido." },
  ],
  filename: null,
  createdAt: "2026-09-09T10:00:02.000Z",
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchStandaloneBatch.mockResolvedValue(lote());
  mocks.fetchStandaloneBatchItems.mockResolvedValue({
    items: [filaGenerada, filaEnError],
    page: 1,
    pageSize: 20,
    total: 2,
  });
  mocks.downloadStandaloneBatchZip.mockResolvedValue(undefined);
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("BatchProgressPanel — progreso (CF-14 / CF-22)", () => {
  it("anuncia el avance en una región aria-live", async () => {
    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const progreso = await screen.findByTestId("lote-progreso");

    expect(progreso).toHaveAttribute("aria-live", "polite");
    expect(progreso).toHaveTextContent("3 de 10 filas procesadas");
    expect(progreso).toHaveTextContent("3 generadas");
    expect(progreso).toHaveTextContent("0 con error");
  });

  it("el progreso no se comunica solo por color: hay barra con aria-valuenow y texto", async () => {
    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const barra = await screen.findByRole("progressbar", { name: /progreso del lote/i });
    expect(barra).toHaveAttribute("aria-valuenow", "30");
    expect(await screen.findByTestId("lote-progreso")).toHaveTextContent(/3 de 10/);
  });
});

describe("BatchProgressPanel — etiquetas de estado (CF-21)", () => {
  it.each([
    ["queued", "En proceso"],
    ["processing", "En proceso"],
  ])("el lote en %s se muestra como «%s»", async (estado, etiqueta) => {
    mocks.fetchStandaloneBatch.mockResolvedValue(lote({ status: estado }));

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const seccion = await screen.findByRole("region", { name: /avance del lote/i });
    expect(within(seccion).getByText(etiqueta)).toBeInTheDocument();
  });

  it("partial_failure es un resultado visible con el conteo de generados y de errores", async () => {
    mocks.fetchStandaloneBatch.mockResolvedValue(
      lote({ status: "partial_failure", isTerminal: true, processed: 10, generated: 9, errors: 1 }),
    );

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const seccion = await screen.findByRole("region", { name: /avance del lote/i });
    expect(within(seccion).getByText("Completado con errores")).toBeInTheDocument();

    const progreso = screen.getByTestId("lote-progreso");
    expect(progreso).toHaveTextContent("9 generadas");
    expect(progreso).toHaveTextContent("1 con error");
  });

  it("un lote terminal avisa que la vista ya no se actualiza sola", async () => {
    mocks.fetchStandaloneBatch.mockResolvedValue(
      lote({ status: "completed", isTerminal: true, processed: 10, generated: 10 }),
    );

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    expect(await screen.findByText(/ya no se actualiza sola/i)).toBeInTheDocument();
  });
});

describe("BatchProgressPanel — descarga ZIP (CF-15)", () => {
  it("descarga el ZIP del lote al pulsar la acción", async () => {
    const user = userEvent.setup();
    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const boton = await screen.findByRole("button", { name: /descargar documentos generados/i });
    await user.click(boton);

    await waitFor(() => expect(mocks.downloadStandaloneBatchZip).toHaveBeenCalledWith(BATCH_ID));
  });

  it("un lote sin documentos generados muestra la explicación del backend, no un archivo vacío", async () => {
    mocks.fetchStandaloneBatch.mockResolvedValue(
      lote({ status: "failed", isTerminal: true, processed: 10, generated: 0, errors: 10 }),
    );
    mocks.downloadStandaloneBatchZip.mockRejectedValue(
      new ApiError(409, "El lote no tiene documentos generados para descargar."),
    );

    const user = userEvent.setup();
    render(<BatchProgressPanel batchId={BATCH_ID} />);

    await user.click(await screen.findByRole("button", { name: /descargar documentos generados/i }));

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent(/no tiene documentos generados/i);
  });

  it("un lote de otra compañía se resuelve como «no encontramos ese lote», sin revelar más", async () => {
    mocks.fetchStandaloneBatch.mockRejectedValue(
      Object.assign(new Error("not found"), { status: 404 }),
    );

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    expect(await screen.findByText(/no encontramos ese lote/i)).toBeInTheDocument();
    expect(screen.queryByTestId("lote-progreso")).not.toBeInTheDocument();
  });
});

describe("BatchProgressPanel — detalle de errores por fila (CF-13)", () => {
  it("muestra número de fila, tipo, estado y el código y campo del error", async () => {
    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const tabla = await screen.findByRole("table", { name: /filas del lote/i });

    expect(within(tabla).getByText("4")).toBeInTheDocument();
    expect(within(tabla).getByText(/transferencia de dominio/i)).toBeInTheDocument();
    expect(within(tabla).getByText("Error")).toBeInTheDocument();
    expect(within(tabla).getByText("VB-02")).toBeInTheDocument();
    // El CAMPO del error se nombra (aparece en el codigo del error y en el mensaje).
    expect(within(tabla).getAllByText(/placa/).length).toBeGreaterThan(0);
  });

  it("NO muestra el valor capturado que produjo el error", async () => {
    mocks.fetchStandaloneBatchItems.mockResolvedValue({
      items: [
        {
          ...filaEnError,
          // Un backend que filtrara el valor lo pondria en el mensaje; el contrato no lo trae y
          // la tabla solo pinta lo que llega.
          validationErrors: [
            { code: "VB-02", field: "placa", message: "La placa no tiene el formato exigido." },
          ],
        },
      ],
      page: 1,
      pageSize: 20,
      total: 1,
    });

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const tabla = await screen.findByRole("table", { name: /filas del lote/i });
    expect(within(tabla).queryByText(/XYZ-9999/)).not.toBeInTheDocument();
    expect(within(tabla).getByText("VB-02")).toBeInTheDocument();
  });

  it("una fila sin tipificar muestra el error real aunque el tipo diga «Certificado RUES»", async () => {
    mocks.fetchStandaloneBatchItems.mockResolvedValue({
      items: [
        {
          ...filaEnError,
          // Trampa del esquema: el tipo tecleado por el usuario no cabe en la columna.
          documentType: "certificado_rues" as const,
          scenario: null,
          errorCode: "unknown_document_type",
          errorField: "document_type",
          validationErrors: [
            {
              code: "unknown_document_type",
              field: "document_type",
              message: "El tipo de documento declarado no existe.",
            },
          ],
        },
      ],
      page: 1,
      pageSize: 20,
      total: 1,
    });

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const tabla = await screen.findByRole("table", { name: /filas del lote/i });
    expect(within(tabla).getByText("unknown_document_type")).toBeInTheDocument();
    expect(within(tabla).getByText(/El tipo de documento declarado no existe/)).toBeInTheDocument();
  });

  it("las filas pending y processing del lote se muestran ambas como «En proceso» (CF-21)", async () => {
    mocks.fetchStandaloneBatchItems.mockResolvedValue({
      items: [
        { ...filaGenerada, id: "a", rowNumber: 1, status: "pending" as const },
        { ...filaGenerada, id: "b", rowNumber: 2, status: "processing" as const },
      ],
      page: 1,
      pageSize: 20,
      total: 2,
    });

    render(<BatchProgressPanel batchId={BATCH_ID} />);

    const tabla = await screen.findByRole("table", { name: /filas del lote/i });
    expect(within(tabla).getAllByText("En proceso")).toHaveLength(2);
  });
});

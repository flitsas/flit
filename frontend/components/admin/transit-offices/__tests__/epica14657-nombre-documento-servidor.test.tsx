// Épica #14657 / HU #14737 — el frontend NO inventa nombres de documentos generados: la fila y el
// archivo descargado usan el nombre que entrega el servidor (`{tipo}_{radicado}_{placa}.pdf`,
// `SIN-PLACA` si no hay placa). Datos ficticios.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtDetalleDocumentos } from "../detalle/OtDetalleDocumentos";
import { downloadFile } from "@/lib/api/download";
import { NOMBRE_GENERICO_CONSOLIDADO } from "@/lib/tramites/consolidado-entrega-ot";

const fetchOtDocuments = vi.fn();
const fetchOtAttachmentPreviewUrl = vi.fn();
const generarOtConsolidadoMaestro = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtDocuments: (...a: unknown[]) => fetchOtDocuments(...a),
  fetchOtAttachmentPreviewUrl: (...a: unknown[]) => fetchOtAttachmentPreviewUrl(...a),
  generarOtConsolidadoMaestro: (...a: unknown[]) => generarOtConsolidadoMaestro(...a),
}));
vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

function adjunto(id: string, filename: string) {
  return {
    id,
    tipo: "consolidado_maestro",
    filename,
    mimetype: "application/pdf",
    sizeBytes: 10,
    sha256: "h",
    source: "system",
    uploadedAt: "2026-11-01T00:00:00Z",
  };
}

function renderTab() {
  render(
    <ToastProvider>
      <OtDetalleDocumentos procedureId="proc-1" readOnly={false} />
    </ToastProvider>,
  );
}

describe("OtDetalleDocumentos — nombre del documento viene del servidor (HU #14737)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtAttachmentPreviewUrl.mockRejectedValue(new Error("sin preview"));
  });

  it.each([
    ["con placa", "consolidado_maestro_RAD-1_ABC123.pdf"],
    ["sin placa", "consolidado_maestro_RAD-2_SIN-PLACA.pdf"],
  ])("fila y archivo comparten el nombre del servidor (%s)", async (_caso, nombre) => {
    fetchOtDocuments.mockResolvedValue({ data: [adjunto("att-1", nombre)] });
    renderTab();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: `Descargar ${nombre}` }));

    await waitFor(() => expect(downloadFile).toHaveBeenCalled());
    const [, opciones] = vi.mocked(downloadFile).mock.calls[0];
    expect(opciones.fallbackFilename).toBe(nombre);
    expect(opciones.fallbackFilename).not.toBe("consolidado-maestro-radicado.pdf");
  });

  it("el nombre genérico de respaldo no es el nombre fijo anterior ni lleva radicado inventado", () => {
    expect(NOMBRE_GENERICO_CONSOLIDADO).not.toMatch(/radicado/i);
    expect(NOMBRE_GENERICO_CONSOLIDADO).toBe("consolidado.pdf");
  });
});

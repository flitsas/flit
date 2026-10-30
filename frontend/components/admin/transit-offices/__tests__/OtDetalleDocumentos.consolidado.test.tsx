// HU #14207 — CTA único «Ver consolidado»: regenera por vigencia (sin force) y previsualiza.
// force=true solo desde «Reintentar» del aviso de fallo (cubierto en hu12799-ot-fallo-regeneracion).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtDetalleDocumentos } from "../detalle/OtDetalleDocumentos";

const fetchOtDocuments = vi.fn();
const generarOtConsolidadoMaestro = vi.fn();
const fetchOtAttachmentPreviewUrl = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtDocuments: (...args: unknown[]) => fetchOtDocuments(...args),
  generarOtConsolidadoMaestro: (...args: unknown[]) => generarOtConsolidadoMaestro(...args),
  fetchOtAttachmentPreviewUrl: (...args: unknown[]) => fetchOtAttachmentPreviewUrl(...args),
}));

vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

const CONSOLIDADO = {
  regenerado: true,
  document: {
    attachmentId: "att-1",
    tipo: "consolidado_maestro",
    filename: "consolidado.pdf",
    sha256: "abc",
  },
};

function renderTab(readOnly = false) {
  render(
    <ToastProvider>
      <OtDetalleDocumentos procedureId="proc-1" readOnly={readOnly} />
    </ToastProvider>,
  );
}

describe("OtDetalleDocumentos — consolidado del expediente (HU #14207)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtDocuments.mockResolvedValue({ data: [] });
    generarOtConsolidadoMaestro.mockResolvedValue(CONSOLIDADO);
    fetchOtAttachmentPreviewUrl.mockRejectedValue(new Error("sin preview en test"));
  });

  it("«Ver consolidado» es el único CTA y no fuerza (vigencia decide)", async () => {
    renderTab();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado/i }));
    await waitFor(() => expect(generarOtConsolidadoMaestro).toHaveBeenCalledWith("proc-1", undefined, false));
    expect(
      screen.queryByRole("button", { name: /Actualizar el consolidado del expediente/i }),
    ).not.toBeInTheDocument();
  });

  it("en modo lectura tampoco ofrece Actualizar; solo Ver consolidado", async () => {
    renderTab(true);

    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
    expect(
      screen.queryByRole("button", { name: /Actualizar el consolidado del expediente/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ver consolidado/i })).toBeInTheDocument();
  });

  it("no queda rastro de CTA «Actualizar» ni «Regenerar» permanente", async () => {
    renderTab();

    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: /Ver consolidado/i })).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Actualizar el consolidado del expediente/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/regenerar/i)).not.toBeInTheDocument();
  });
});

// Feature #10701 / HU #10860 — la salida manual del organismo reconstruye el expediente
// consolidado ignorando la marca de vigencia. Existe porque el consolidado se sirve cacheado, y
// aunque cualquier cambio del expediente lo invalide, el operador que dude de lo que ve no puede
// quedarse sin forma de comprobarlo.
//
// La cobertura vivía en ClientProcedureDetailPanel.regenerar.test.tsx; con la HU #11930 la acción
// dejó de estar en el pie del detalle y quedó solo aquí, dentro de la sección Documentos.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtDocumentosTab } from "../OtDocumentosTab";

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
      <OtDocumentosTab procedureId="proc-1" referenceNumber="RAD-0001" readOnly={readOnly} />
    </ToastProvider>,
  );
}

describe("OtDocumentosTab — consolidado del expediente", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtDocuments.mockResolvedValue({ data: [] });
    generarOtConsolidadoMaestro.mockResolvedValue(CONSOLIDADO);
    // La previsualización descarga los bytes; sin URL el flujo termina en el aviso de error, que no
    // es lo que mide este test.
    fetchOtAttachmentPreviewUrl.mockRejectedValue(new Error("sin preview en test"));
  });

  it("«Ver consolidado» no fuerza y «Actualizar consolidado» sí (HU #11932)", async () => {
    renderTab();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Ver consolidado del expediente/i }));
    // Sin forzar: el backend decide por la marca de vigencia.
    await waitFor(() => expect(generarOtConsolidadoMaestro).toHaveBeenCalledWith("proc-1", undefined, false));

    await user.click(screen.getByRole("button", { name: /Actualizar el consolidado del expediente/i }));
    await waitFor(() =>
      expect(generarOtConsolidadoMaestro).toHaveBeenLastCalledWith("proc-1", undefined, true),
    );
  });

  it("en modo lectura no ofrece la salida manual", async () => {
    renderTab(true);

    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
    expect(
      screen.queryByRole("button", { name: /Actualizar el consolidado del expediente/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ver consolidado del expediente/i })).toBeInTheDocument();
  });

  it("HU #11932 — la acción se llama «Actualizar consolidado», sin rastro de «Regenerar»", async () => {
    renderTab();

    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());

    const accion = screen.getByRole("button", { name: /Actualizar el consolidado del expediente/i });
    // El texto visible y el nombre accesible dicen lo mismo: el PDF ya existe y se reconstruye.
    expect(accion).toHaveTextContent("Actualizar consolidado");
    expect(accion).toHaveAttribute(
      "title",
      "Reconstruye el expediente consolidado con el contenido actual del trámite",
    );
    expect(screen.queryByText(/regenerar/i)).not.toBeInTheDocument();
  });
});

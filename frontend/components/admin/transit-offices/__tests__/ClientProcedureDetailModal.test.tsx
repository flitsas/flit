// HU #11930 — el detalle del trámite del OT es un modal con secciones, no un panel lateral, y es
// de solo lectura. Sustituye a ClientProcedureDetailPanel.{regenerar,observaciones}.test.tsx: la
// acción de consolidado se mudó a la sección Documentos (ver OtDocumentosTab.consolidado.test.tsx).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProcedureDetailModal } from "../ClientProcedureDetailModal";
import type { OtClientProcedure } from "@/lib/api/types-ot";

const fetchOtClientProcedure = vi.fn();
const fetchOtDocuments = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedure: (...args: unknown[]) => fetchOtClientProcedure(...args),
  fetchOtDocuments: (...args: unknown[]) => fetchOtDocuments(...args),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
}));

const PROCEDURE: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Traspaso",
  clientTenantName: "Empresa Demo",
  referenceNumber: "RAD-0001",
  status: "entregado",
  plateFlowStatus: null,
  soatEstado: "vigente",
  createdAt: "2026-08-01T00:00:00Z",
  placa: "ABC123",
  vin: "VIN-9",
  marca: "Renault",
  linea: "Logan",
  clase: "AUTOMOVIL",
  cilindraje: "1600",
  numeroMotor: "MOT-1",
  actors: [
    { actorType: "comprador", documentType: "CC", documentNumber: "1", fullName: "Ana Compradora" },
    { actorType: "vendedor", documentType: "CC", documentNumber: "2", fullName: "Beto Vendedor" },
  ],
  comercial: { valorVenta: 45000000, causal: "COMPRAVENTA", metodoPago: "TRANSFERENCIA" },
};

function renderModal(procedure: OtClientProcedure = PROCEDURE, onClose = vi.fn()) {
  render(<ClientProcedureDetailModal open procedure={procedure} onClose={onClose} />);
  return { onClose };
}

describe("ClientProcedureDetailModal (HU #11930)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtClientProcedure.mockResolvedValue(PROCEDURE);
    fetchOtDocuments.mockResolvedValue({ data: [{ id: "d1", tipo: "fur", filename: "fur.pdf" }] });
  });

  it("AC1 — se presenta como diálogo con radicado, placa y estado en el encabezado", async () => {
    renderModal();

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toBeInTheDocument();
    // El radicado sale dos veces: en el encabezado y en la ficha del trámite.
    expect(screen.getAllByText("RAD-0001").length).toBeGreaterThan(0);
    expect(dialog).toHaveTextContent("ABC123");
    // El estado también sale dos veces: chip del encabezado y campo de la ficha.
    expect(screen.getAllByText("Pendiente OT").length).toBeGreaterThan(0);
  });

  it("AC2 — recorre las secciones sin cerrar el modal", async () => {
    renderModal();
    const user = userEvent.setup();

    // Arranca en «Trámite y vehículo».
    expect(await screen.findByText("Datos del trámite")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /Actores/i }));
    expect(await screen.findByText("Ana Compradora")).toBeInTheDocument();
    expect(screen.getByText("Beto Vendedor")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /Datos comerciales/i }));
    expect(await screen.findByText("Operación comercial")).toBeInTheDocument();
    expect(screen.getByText("Compraventa")).toBeInTheDocument();

    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("AC2 — muestra las especificaciones técnicas y omite las que el trámite no tiene", async () => {
    renderModal();

    expect(await screen.findByText("Especificaciones del vehículo")).toBeInTheDocument();
    expect(screen.getByText("1600 cc")).toBeInTheDocument();
    expect(screen.getByText("MOT-1")).toBeInTheDocument();
    // Sin número de chasis capturado, la fila no se pinta (no se inventa ni se rellena con otra).
    expect(screen.queryByText("N. Chasis")).not.toBeInTheDocument();
  });

  it("AC4 — no ofrece ninguna acción de captura o edición del trámite", async () => {
    renderModal();

    await screen.findByText("Datos del trámite");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /guardar|editar|aprobar|rechazar/i })).not.toBeInTheDocument();
  });

  it("AC5 — cierra con el control de cierre y con Escape", async () => {
    const { onClose } = renderModal();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Cerrar" }));
    expect(onClose).toHaveBeenCalledTimes(1);

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("AC6 — una sección sin datos muestra su vacío, no un error", async () => {
    const sinComercial = { ...PROCEDURE, comercial: null, prenda: null };
    fetchOtClientProcedure.mockResolvedValue(sinComercial);
    renderModal(sinComercial);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("tab", { name: /Datos comerciales/i }));
    expect(
      await screen.findByText(/no tiene datos comerciales ni decisión de prenda/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("abre en la sección Documentos cuando se entra por «ver documentos»", async () => {
    render(
      // La sección Documentos emite avisos (toast) al actualizar el consolidado.
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={PROCEDURE}
          onClose={vi.fn()}
          initialSection="documentos"
        />
      </ToastProvider>,
    );

    expect(await screen.findByTestId("ot-documentos-tab")).toBeInTheDocument();
  });

  it("Bug #11585 — sin pendientes no monta el bloque de pendientes", async () => {
    renderModal();

    await screen.findByText("Datos del trámite");
    expect(screen.queryByText("Pendientes antes de decidir")).not.toBeInTheDocument();
  });

  it("Bug #11585 — con expediente vacío y SOAT no vigente sí los enumera", async () => {
    const conPendientes = { ...PROCEDURE, soatEstado: "vencido" };
    fetchOtClientProcedure.mockResolvedValue(conPendientes);
    fetchOtDocuments.mockResolvedValue({ data: [] });
    renderModal(conPendientes);

    expect(await screen.findByText("Pendientes antes de decidir")).toBeInTheDocument();
    expect(screen.getByText(/SOAT RUNT no vigente/i)).toBeInTheDocument();
    expect(screen.getByText(/expediente aún no tiene documentos/i)).toBeInTheDocument();
  });

  it("si el refresco del detalle falla, avisa y conserva los datos de la bandeja", async () => {
    fetchOtClientProcedure.mockRejectedValue(new Error("boom"));
    renderModal();

    await waitFor(() =>
      expect(screen.getByText(/se muestran los datos de la bandeja/i)).toBeInTheDocument(),
    );
    expect(screen.getAllByText("RAD-0001").length).toBeGreaterThan(0);
  });
});

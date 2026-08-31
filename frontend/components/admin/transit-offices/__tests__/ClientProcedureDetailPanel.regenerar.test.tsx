// Feature #10701 / HU #10860 — "Regenerar" es la salida manual del organismo: reconstruye el
// expediente consolidado ignorando la marca de vigencia. Existe porque el consolidado se sirve
// cacheado, y aunque ahora cualquier cambio del expediente lo invalida, el operador que dude de lo
// que está viendo no puede quedarse sin forma de comprobarlo.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ClientProcedureDetailPanel } from "../ClientProcedureDetailPanel";
import type { OtClientProcedure } from "@/lib/api/types-ot";

const fetchOtClientProcedure = vi.fn();
const fetchOtDocuments = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedure: (...args: unknown[]) => fetchOtClientProcedure(...args),
  fetchOtDocuments: (...args: unknown[]) => fetchOtDocuments(...args),
}));

const PROCEDURE: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Matrícula inicial",
  clientTenantName: "Empresa Demo",
  referenceNumber: "RAD-0001",
  status: "entregado",
  plateFlowStatus: null,
  soatEstado: "vigente",
  createdAt: "2026-08-01T00:00:00Z",
  actors: [{ actorType: "comprador", documentType: "CC", documentNumber: "1", fullName: "Ana" }],
};

describe("ClientProcedureDetailPanel — Regenerar consolidado", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtClientProcedure.mockResolvedValue(PROCEDURE);
    fetchOtDocuments.mockResolvedValue({ data: [] });
  });

  it("«Ver consolidado» no fuerza y «Regenerar» sí", async () => {
    const onVerConsolidado = vi.fn();
    render(
      <ClientProcedureDetailPanel
        open
        procedure={PROCEDURE}
        onClose={vi.fn()}
        onVerConsolidado={onVerConsolidado}
      />,
    );

    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /^Ver consolidado$/i }));
    // Sin segundo argumento: el backend decide por la marca de vigencia.
    expect(onVerConsolidado).toHaveBeenCalledWith(PROCEDURE);

    await user.click(screen.getByRole("button", { name: /^Regenerar$/i }));
    expect(onVerConsolidado).toHaveBeenLastCalledWith(PROCEDURE, true);
  });

  it("no ofrece «Regenerar» cuando el trámite no admite consolidado", async () => {
    // Mismo criterio que «Ver consolidado»: solo entregado o aprobado. Un borrador del cliente no
    // tiene expediente que el organismo pueda consolidar. El estado que manda es el del detalle que
    // trae el panel, no el de la fila con la que se abrió.
    fetchOtClientProcedure.mockResolvedValue({ ...PROCEDURE, status: "borrador" });
    render(
      <ClientProcedureDetailPanel
        open
        procedure={{ ...PROCEDURE, status: "borrador" }}
        onClose={vi.fn()}
        onVerConsolidado={vi.fn()}
      />,
    );

    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: /^Regenerar$/i })).not.toBeInTheDocument();
  });
});

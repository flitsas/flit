// Bug #11585 — el bloque "Observaciones / pendientes" no debe montarse cuando no tiene
// ningún ítem que mostrar (sin documentos, sin placa preasignada/asignada, SOAT vigente).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
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

describe("ClientProcedureDetailPanel — Observaciones / pendientes (Bug #11585)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtClientProcedure.mockResolvedValue(PROCEDURE);
    fetchOtDocuments.mockResolvedValue({ data: [] });
  });

  it("no monta la sección cuando no hay documentos, placa preasignada/asignada ni SOAT no vigente", async () => {
    render(
      <ClientProcedureDetailPanel open procedure={PROCEDURE} onClose={vi.fn()} />,
    );

    await waitFor(() => expect(fetchOtDocuments).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.queryByText(/aún no hay documentos en el expediente/i)).toBeInTheDocument(),
    );

    expect(screen.queryByText("Observaciones / pendientes")).not.toBeInTheDocument();
  });
});

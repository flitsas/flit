import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ClientProceduresTable } from "../ClientProceduresTable";
import type { OtClientProcedure } from "@/lib/api/types-ot";

// HU #10804 — en la bandeja del OT, Aprobar y Rechazar se ocultan JUNTOS en la ruta de placa hasta que
// la placa esté 'asignado' con el SOAT 'vigente'. En ruta estándar se muestran como siempre.
// Uso de ejemplo: <ClientProceduresTable rows={[{ plateFlowStatus: 'asignado', soatEstado: 'vigente' }]} />

function row(over: Partial<OtClientProcedure>): OtClientProcedure {
  return {
    id: "id-1",
    clientTenantId: "c1",
    procedureTypeId: "t1",
    referenceNumber: "TRM-1",
    status: "entregado",
    createdAt: "2026-07-17T00:00:00Z",
    ...over,
  };
}

const baseProps = {
  totalCount: 1,
  page: 1,
  pageSize: 20,
  onPageChange: vi.fn(),
  onApprove: vi.fn(),
  onReject: vi.fn(),
  showApprovalActions: true,
  onAssignPlate: vi.fn(),
  onRevoke: vi.fn(),
};

describe("ClientProceduresTable — gate visual de SOAT (HU #10804)", () => {
  // AC1 — asignado con SOAT vigente: acciones visibles, sin aviso.
  it("muestra Aprobar y Rechazar cuando la placa está asignada y el SOAT vigente", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "asignado", soatEstado: "vigente" })]}
      />,
    );
    expect(screen.getByRole("button", { name: /aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /rechazar/i })).toBeInTheDocument();
    expect(screen.queryByText(/esperando validación de soat/i)).not.toBeInTheDocument();
  });

  // AC3 — asignado sin SOAT vigente: acciones ocultas + aviso.
  it("oculta las acciones y muestra el aviso cuando la placa está asignada sin SOAT vigente", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "asignado", soatEstado: null })]}
      />,
    );
    expect(screen.queryByRole("button", { name: /aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /rechazar/i })).not.toBeInTheDocument();
    expect(
      screen.getByText(/esperando validación de soat del gestor/i),
    ).toBeInTheDocument();
  });

  // AC2 — preasignado: acciones ocultas, sin aviso de SOAT (aún falta asignar placa).
  it("oculta Aprobar y Rechazar en preasignado y no muestra el aviso de SOAT", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "preasignado", soatEstado: null })]}
      />,
    );
    expect(screen.queryByRole("button", { name: /aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /rechazar/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/esperando validación de soat/i)).not.toBeInTheDocument();
  });

  // AC4 — ruta estándar (sin sub-estado de placa): acciones visibles como hoy.
  it("muestra Aprobar y Rechazar en la ruta estándar (sin sub-estado de placa)", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: null, soatEstado: null })]}
      />,
    );
    expect(screen.getByRole("button", { name: /aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /rechazar/i })).toBeInTheDocument();
  });
});

import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ClientProceduresTable } from "../ClientProceduresTable";
import type { OtClientProcedure } from "@/lib/api/types-ot";

// Gate visual OT: Aprobar/Rechazar solo en ruta estándar o Terminado.
// Uso: <ClientProceduresTable rows={[{ plateFlowStatus: 'terminado' }]} />

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

describe("ClientProceduresTable — gate visual Terminado", () => {
  it("muestra Aprobar y Rechazar en Terminado", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "terminado", soatPagado: true })]}
      />,
    );
    expect(screen.getByRole("button", { name: /aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /rechazar/i })).toBeInTheDocument();
    expect(screen.getByText("SOAT")).toBeInTheDocument();
    expect(screen.queryByText(/esperando proceso del gestor/i)).not.toBeInTheDocument();
  });

  it("oculta acciones y muestra aviso en Asignado", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "asignado" })]}
      />,
    );
    expect(screen.queryByRole("button", { name: /aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /rechazar/i })).not.toBeInTheDocument();
    expect(screen.getByText(/esperando proceso del gestor/i)).toBeInTheDocument();
  });

  it("oculta Aprobar/Rechazar en Sin asignar (preasignado) sin aviso de gestor", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "preasignado" })]}
      />,
    );
    expect(screen.queryByRole("button", { name: /aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /rechazar/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/esperando proceso del gestor/i)).not.toBeInTheDocument();
  });

  it("muestra Aprobar y Rechazar en la ruta estándar", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: null })]}
      />,
    );
    expect(screen.getByRole("button", { name: /aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /rechazar/i })).toBeInTheDocument();
  });

  it("no muestra badges SOAT/Impuesto fuera de Terminado", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "asignado", soatPagado: true, impuestoDepartamentalPagado: true })]}
      />,
    );
    expect(screen.queryByText("SOAT")).not.toBeInTheDocument();
    expect(screen.queryByText("Impuesto")).not.toBeInTheDocument();
  });
});

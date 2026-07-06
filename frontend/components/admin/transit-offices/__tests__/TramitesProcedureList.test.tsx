// HU #10218 — TramitesProcedureList: acciones Aprobar/Rechazar solo en estado "entregado".
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { TramitesProcedureList } from "../TramitesProcedureList";
import type { OtClientProcedure } from "@/lib/api/types-ot";

function makeProcedure(overrides: Partial<OtClientProcedure> = {}): OtClientProcedure {
  return {
    id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    clientTenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
    procedureTypeId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
    referenceNumber: "REF-001",
    status: "entregado",
    createdAt: "2026-06-23T12:00:00Z",
    ...overrides,
  };
}

describe("TramitesProcedureList", () => {
  it("muestra Aprobar/Rechazar cuando el trámite está entregado y hay permisos", () => {
    render(
      <TramitesProcedureList
        procedures={[makeProcedure()]}
        showApprovalActions
        onApprove={vi.fn()}
        onReject={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /Aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Rechazar/i })).toBeInTheDocument();
  });

  it("oculta las acciones si el trámite ya no está entregado aunque showApprovalActions sea true", () => {
    render(
      <TramitesProcedureList
        procedures={[makeProcedure({ status: "aprobado" })]}
        showApprovalActions
        onApprove={vi.fn()}
        onReject={vi.fn()}
      />,
    );

    expect(screen.queryByRole("button", { name: /Aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Rechazar/i })).not.toBeInTheDocument();
  });
});

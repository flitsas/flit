import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ClientProceduresTable } from "../ClientProceduresTable";
import type { OtClientProcedure } from "@/lib/api/types-ot";

const ROW: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Matrícula inicial",
  clientTenantName: "Empresa Demo",
  referenceNumber: "FT1-0000230",
  status: "borrador",
  createdAt: "2026-10-28T15:21:00Z",
  gestorNombre: "Jose Manuel Pimienta",
};

describe("ClientProceduresTable — HU #14157 timeline desde Estado", () => {
  it("AC1 — clic en el chip de Estado abre el detalle con panel timeline", async () => {
    const onVerDetalle = vi.fn();
    const user = userEvent.setup();

    render(
      <ClientProceduresTable
        rows={[ROW]}
        totalCount={1}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onPageSizeChange={vi.fn()}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        showApprovalActions={false}
        onVerDetalle={onVerDetalle}
      />,
    );

    await user.click(
      screen.getByRole("button", {
        name: /Estado: Borrador\. Ver línea de tiempo del trámite FT1-0000230/i,
      }),
    );

    expect(onVerDetalle).toHaveBeenCalledTimes(1);
    expect(onVerDetalle).toHaveBeenCalledWith(ROW, { panel: "timeline" });
  });
});

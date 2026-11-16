import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ClientProceduresTable } from "../ClientProceduresTable";
import type { OtClientProcedure } from "@/lib/api/types-ot";

// Uso de ejemplo: <ClientProceduresTable rows={[ROW]} onVerDetalle={fn} … /> — solo el botón del
// radicado abre el detalle; el resto de la fila se puede seleccionar y copiar sin abrir nada.

const ROW: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Matrícula inicial",
  clientTenantName: "Empresa Demo",
  referenceNumber: "FT1-0000230",
  status: "borrador",
  createdAt: "2026-10-28T15:21:00Z",
  gestorNombre: "Gestor De Prueba",
  placa: "ABC123",
};

function renderTabla(onVerDetalle = vi.fn()) {
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
  return onVerDetalle;
}

const fila = () => screen.getAllByRole("row")[1];

describe("ClientProceduresTable — ajuste 2026-11-10: copiar sin abrir, solo el radicado abre", () => {
  it("clic en una celda que no es el radicado NO abre el detalle", async () => {
    const onVerDetalle = renderTabla();
    await userEvent.click(within(fila()).getByText("Matrícula inicial"));
    expect(onVerDetalle).not.toHaveBeenCalled();
  });

  it("la fila ya no es un botón ni se enfoca: es una fila de tabla con cursor normal", () => {
    renderTabla();
    const tr = fila();
    expect(tr).not.toHaveAttribute("role", "button");
    expect(tr).not.toHaveAttribute("tabindex");
    expect(tr.className).not.toMatch(/cursor-pointer/);
  });

  it("clic en el radicado abre el detalle de esa fila", async () => {
    const onVerDetalle = renderTabla();
    await userEvent.click(
      screen.getByRole("button", { name: "Ver detalle del trámite FT1-0000230" }),
    );
    expect(onVerDetalle).toHaveBeenCalledTimes(1);
    expect(onVerDetalle).toHaveBeenCalledWith(ROW);
  });

  it("Enter y Espacio sobre el botón del radicado abren el detalle", async () => {
    const onVerDetalle = renderTabla();
    const boton = screen.getByRole("button", { name: "Ver detalle del trámite FT1-0000230" });
    boton.focus();
    await userEvent.keyboard("{Enter}");
    await userEvent.keyboard(" ");
    expect(onVerDetalle).toHaveBeenCalledTimes(2);
  });

  it("sin onVerDetalle el radicado es texto plano, no un botón", () => {
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
      />,
    );
    expect(screen.getByText("FT1-0000230")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Ver detalle del trámite FT1-0000230/ }),
    ).not.toBeInTheDocument();
  });
});

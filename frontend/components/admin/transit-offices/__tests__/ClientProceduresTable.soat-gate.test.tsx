import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

/** Abre el menú de acciones de la primera fila: la decisión del OT vive ahí. */
async function abrirAcciones() {
  await userEvent.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
}

describe("ClientProceduresTable — gate visual Terminado", () => {
  it("muestra Aprobar y Rechazar en Terminado", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "terminado", soatPagado: true })]}
      />,
    );
    await abrirAcciones();
    expect(screen.getByRole("menuitem", { name: /aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /rechazar/i })).toBeInTheDocument();
    // El sello de SOAT y el aviso de proceso NO son acciones: siguen a la vista en la fila.
    expect(screen.getByText("SOAT")).toBeInTheDocument();
    expect(screen.queryByText(/esperando proceso del gestor/i)).not.toBeInTheDocument();
  });

  it("oculta acciones y muestra aviso en Asignado", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "asignado" })]}
      />,
    );
    await abrirAcciones();
    expect(screen.queryByRole("menuitem", { name: /aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /rechazar/i })).not.toBeInTheDocument();
    expect(screen.getByText(/esperando proceso del gestor/i)).toBeInTheDocument();
  });

  it("oculta Aprobar/Rechazar en Sin asignar (preasignado) sin aviso de gestor", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: "preasignado" })]}
      />,
    );
    await abrirAcciones();
    expect(screen.queryByRole("menuitem", { name: /aprobar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /rechazar/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/esperando proceso del gestor/i)).not.toBeInTheDocument();
  });

  it("muestra Aprobar y Rechazar en la ruta estándar", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ plateFlowStatus: null })]}
      />,
    );
    await abrirAcciones();
    expect(screen.getByRole("menuitem", { name: /aprobar/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /rechazar/i })).toBeInTheDocument();
  });

  it("no muestra badges SOAT/Impuesto fuera de Terminado", async () => {
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

describe("ClientProceduresTable — columnas VIN/placa/actores/gestor", () => {
  it("renderiza las nuevas columnas con los valores del listado", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        onSortChange={vi.fn()}
        rows={[
          row({
            vin: "9BWZZZ377VT004251",
            placa: "ABC123",
            vendedorNombre: "Ana Vendedora",
            compradorNombre: "Luis Comprador",
            gestorNombre: "Carlos Gestor",
          }),
        ]}
      />,
    );

    expect(screen.getByRole("button", { name: /Ordenar por VIN/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ordenar por Placa/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ordenar por Propietario \/ vendedor/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ordenar por Comprador/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ordenar por Gestor/i })).toBeInTheDocument();

    expect(screen.getByText("9BWZZZ377VT004251")).toBeInTheDocument();
    expect(screen.getByText("ABC123")).toBeInTheDocument();
    expect(screen.getByText("Ana Vendedora")).toBeInTheDocument();
    expect(screen.getByText("Luis Comprador")).toBeInTheDocument();
    expect(screen.getByText("Carlos Gestor")).toBeInTheDocument();
  });

  it("al clicar una cabecera ordenable notifica sortBy/sortDir", async () => {
    const user = (await import("@testing-library/user-event")).default.setup();
    const onSortChange = vi.fn();
    render(
      <ClientProceduresTable
        {...baseProps}
        sortBy="createdAt"
        sortDir="desc"
        onSortChange={onSortChange}
        rows={[row({ placa: "XYZ999" })]}
      />,
    );

    await user.click(screen.getByRole("button", { name: /Ordenar por Placa/i }));
    expect(onSortChange).toHaveBeenCalledWith("placa", "asc");
  });
});

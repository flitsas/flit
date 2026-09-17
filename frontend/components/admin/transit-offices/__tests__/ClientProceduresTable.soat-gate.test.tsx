import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ClientProceduresTable } from "../ClientProceduresTable";
import type { OtClientProcedure } from "@/lib/api/types-ot";

// ADR-0059 (HU #12602 AC3–AC5) — el menú por fila ofrece SOLO lo que aplica en cada estado real:
// entregado decide; preasignacion asigna placa o rechaza; asignado corrige o libera la placa.

function row(over: Partial<OtClientProcedure>): OtClientProcedure {
  return {
    id: "id-1",
    clientTenantId: "c1",
    procedureTypeId: "t1",
    referenceNumber: "FT1-0000001",
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
  onUpdatePlate: vi.fn(),
  onAdjuntarLt: vi.fn(),
};

/** Abre el menú de acciones de la primera fila: la decisión del OT vive ahí. */
async function abrirAcciones() {
  await userEvent.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
}

describe("ClientProceduresTable — acciones por estado real (ADR-0059)", () => {
  it("AC5 — en entregado ofrece Aprobar y Rechazar y muestra los checks del gestor; sin chip «Terminado» ni tooltip", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ status: "entregado", soatPagado: true, impuestoDepartamentalPagado: true })]}
      />,
    );
    await abrirAcciones();
    expect(screen.getByRole("menuitem", { name: /^aprobar$/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /^rechazar$/i })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /asignar placa/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /liberar placa/i })).not.toBeInTheDocument();
    // Los checks del gestor NO son acciones: siguen a la vista en la fila.
    expect(screen.getByText("SOAT")).toBeInTheDocument();
    expect(screen.getByText("Impuesto")).toBeInTheDocument();
    expect(screen.queryByText(/terminado/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/esperando proceso del gestor/i)).not.toBeInTheDocument();
  });

  it("AC4 — en asignado ofrece Actualizar placa (con minutos) y Liberar placa; ni Aprobar, ni Rechazar, ni Adjuntar LT", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ status: "asignado", placa: "ABC123", plateAssignedAt: new Date().toISOString() })]}
      />,
    );
    await abrirAcciones();
    expect(screen.getByRole("menuitem", { name: /actualizar placa \(\d+ min\)/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /liberar placa/i })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /^aprobar$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /^rechazar$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /adjuntar lt/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/esperando proceso del gestor/i)).not.toBeInTheDocument();
  });

  it("AC3 — en preasignacion ofrece Asignar placa y Rechazar; ni Aprobar ni Adjuntar LT", async () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ status: "preasignacion", placa: null })]}
      />,
    );
    await abrirAcciones();
    expect(screen.getByRole("menuitem", { name: /asignar placa/i })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /^rechazar$/i })).toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /^aprobar$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /adjuntar lt/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: /liberar placa/i })).not.toBeInTheDocument();
  });

  it("el chip de estado usa el nombre real (Preasignación / Asignado), sin sufijo «OT» ni badge secundario", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ status: "preasignacion" }), row({ id: "id-2", referenceNumber: "FT1-0000002", status: "asignado" })]}
      />,
    );
    expect(screen.getByText("Preasignación")).toBeInTheDocument();
    expect(screen.getByText("Asignado")).toBeInTheDocument();
    expect(screen.queryByText(/sin asignar/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/pendiente ot/i)).not.toBeInTheDocument();
  });

  it("no muestra badges SOAT/Impuesto fuera de entregado", () => {
    render(
      <ClientProceduresTable
        {...baseProps}
        rows={[row({ status: "asignado", soatPagado: true, impuestoDepartamentalPagado: true })]}
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
    // HU #12219 — «Empresa / Gestor» apila dos datos, así que su cabecera dejó de ser un clic
    // simple (que solo sabía ordenar por gestor, prometiendo un orden por empresa inexistente) y
    // pasó a ser un desplegable con las dos opciones.
    expect(screen.getByRole("button", { name: /Ordenar Empresa \/ Gestor/i })).toBeInTheDocument();

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

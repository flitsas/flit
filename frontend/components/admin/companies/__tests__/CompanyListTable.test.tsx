// AC1 — Listado con columnas y paginación server-side.
//
// Uso de ejemplo:
//   render(<CompanyListTable items={items} totalCount={40} page={1} pageSize={20} ... />);
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CompanyListTable } from "../CompanyListTable";
import type { CompanyListItem } from "@/lib/api/types";

const items: CompanyListItem[] = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    nit: "900.123.456-7",
    razonSocial: "FLIT SAS",
    code: "FLITSAS",
    tenantType: "FLIT",
    estadoActivo: true,
    fechaCreacion: "2026-01-15T10:00:00Z",
    rowVersion: 1,
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    nit: "890.456.789-1",
    razonSocial: "Movilidad Antioquia",
    code: "MOVANT",
    tenantType: "RENTING",
    estadoActivo: false,
    fechaCreacion: "2026-02-20T10:00:00Z",
    rowVersion: 2,
  },
];

describe("CompanyListTable (AC1)", () => {
  it("renderiza las columnas y las filas", () => {
    render(
      <CompanyListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onConfigure={vi.fn()}
        onEdit={vi.fn()}
        onToggleStatus={vi.fn()}
      />,
    );

    expect(screen.getByText("NIT")).toBeInTheDocument();
    expect(screen.getByText("Razón Social")).toBeInTheDocument();
    expect(screen.getByText("Estado")).toBeInTheDocument();
    expect(screen.getByText("Fecha creación")).toBeInTheDocument();

    expect(screen.getByText("FLIT SAS")).toBeInTheDocument();
    expect(screen.getByText("Movilidad Antioquia")).toBeInTheDocument();
    expect(screen.getByText("Activa")).toBeInTheDocument();
    expect(screen.getByText("Inactiva")).toBeInTheDocument();
  });

  it("dispara onPageChange con la siguiente página (server-side)", () => {
    const onPageChange = vi.fn();
    render(
      <CompanyListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={onPageChange}
        onConfigure={vi.fn()}
        onEdit={vi.fn()}
        onToggleStatus={vi.fn()}
      />,
    );

    expect(screen.getByText("1 / 2")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /página siguiente/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("deshabilita 'Anterior' en la primera página", () => {
    render(
      <CompanyListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onConfigure={vi.fn()}
        onEdit={vi.fn()}
        onToggleStatus={vi.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /página anterior/i })).toBeDisabled();
  });

  it("dispara onConfigure con el id de la compañía", () => {
    const onConfigure = vi.fn();
    render(
      <CompanyListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onConfigure={onConfigure}
        onEdit={vi.fn()}
        onToggleStatus={vi.fn()}
      />,
    );
    fireEvent.click(screen.getAllByRole("button", { name: /configurar/i })[0]);
    expect(onConfigure).toHaveBeenCalledWith(items[0].id);
  });

  it("dispara onEdit con la compañía al pulsar Editar", () => {
    const onEdit = vi.fn();
    render(
      <CompanyListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onConfigure={vi.fn()}
        onEdit={onEdit}
        onToggleStatus={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /editar FLIT SAS/i }));
    expect(onEdit).toHaveBeenCalledWith(items[0]);
  });

  it("deshabilita Editar en compañías de tipo heredado (no-B2B) y no dispara onEdit", () => {
    const onEdit = vi.fn();
    const legacy: CompanyListItem[] = [
      {
        id: "33333333-3333-3333-3333-333333333333",
        nit: "999.999.999-9",
        razonSocial: "Empresa Demo FLIT",
        code: "DEMO",
        tenantType: "standard",
        estadoActivo: true,
        fechaCreacion: "2026-03-10T10:00:00Z",
        rowVersion: 3,
      },
    ];
    render(
      <CompanyListTable
        items={legacy}
        totalCount={1}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onConfigure={vi.fn()}
        onEdit={onEdit}
        onToggleStatus={vi.fn()}
      />,
    );

    const editBtn = screen.getByRole("button", { name: /editar Empresa Demo FLIT/i });
    expect(editBtn).toBeDisabled();
    fireEvent.click(editBtn);
    expect(onEdit).not.toHaveBeenCalled();
  });

  it("dispara onToggleStatus con la compañía al pulsar Activar/Desactivar", () => {
    const onToggleStatus = vi.fn();
    render(
      <CompanyListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onConfigure={vi.fn()}
        onEdit={vi.fn()}
        onToggleStatus={onToggleStatus}
      />,
    );

    // La primera compañía está activa → botón "Desactivar"; la segunda inactiva → "Activar".
    fireEvent.click(screen.getByRole("button", { name: /desactivar FLIT SAS/i }));
    expect(onToggleStatus).toHaveBeenCalledWith(items[0]);

    fireEvent.click(screen.getByRole("button", { name: /activar Movilidad Antioquia/i }));
    expect(onToggleStatus).toHaveBeenCalledWith(items[1]);
  });
});

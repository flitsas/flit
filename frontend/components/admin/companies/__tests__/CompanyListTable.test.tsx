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
    estadoActivo: true,
    fechaCreacion: "2026-01-15T10:00:00Z",
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    nit: "890.456.789-1",
    razonSocial: "Movilidad Antioquia",
    estadoActivo: false,
    fechaCreacion: "2026-02-20T10:00:00Z",
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
        onToggleStatus={vi.fn()}
      />,
    );
    fireEvent.click(screen.getAllByRole("button", { name: /configurar/i })[0]);
    expect(onConfigure).toHaveBeenCalledWith(items[0].id);
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

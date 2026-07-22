// HU #10844 — DataTable canónico: cabecera, filas, estado vacío, paginación y fila expandible.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DataTable, type DataTableColumn } from "../DataTable";

type Row = { id: string; nombre: string };
const columns: DataTableColumn<Row>[] = [
  { key: "nombre", header: "Nombre", render: (r) => r.nombre },
  { key: "acc", header: "Acciones", align: "right", render: () => "…" },
];
const rows: Row[] = [
  { id: "1", nombre: "Alfa" },
  { id: "2", nombre: "Beta" },
];

describe("DataTable", () => {
  it("renderiza cabeceras y filas", () => {
    render(<DataTable columns={columns} rows={rows} getRowKey={(r) => r.id} />);
    expect(screen.getByText("Nombre")).toBeInTheDocument();
    expect(screen.getByText("Alfa")).toBeInTheDocument();
    expect(screen.getByText("Beta")).toBeInTheDocument();
  });

  it("muestra el estado vacío cuando no hay filas", () => {
    render(
      <DataTable
        columns={columns}
        rows={[]}
        getRowKey={(r) => r.id}
        emptyMessage="Sin registros."
      />,
    );
    expect(screen.getByText("Sin registros.")).toBeInTheDocument();
  });

  it("dispara onRowClick", async () => {
    const onRowClick = vi.fn();
    render(
      <DataTable columns={columns} rows={rows} getRowKey={(r) => r.id} onRowClick={onRowClick} />,
    );
    await userEvent.click(screen.getByText("Alfa"));
    expect(onRowClick).toHaveBeenCalledWith(rows[0]);
  });

  it("renderiza contenido expandible bajo la fila", () => {
    render(
      <DataTable
        columns={columns}
        rows={rows}
        getRowKey={(r) => r.id}
        renderExpanded={(r) => (r.id === "1" ? <span>detalle-alfa</span> : null)}
      />,
    );
    expect(screen.getByText("detalle-alfa")).toBeInTheDocument();
  });

  it("muestra la paginación cuando se provee", () => {
    render(
      <DataTable
        columns={columns}
        rows={rows}
        getRowKey={(r) => r.id}
        pagination={{ page: 1, pageSize: 10, totalCount: 2, onPageChange: vi.fn() }}
      />,
    );
    expect(screen.getByText(/Mostrando 1–2 de 2/)).toBeInTheDocument();
  });
});

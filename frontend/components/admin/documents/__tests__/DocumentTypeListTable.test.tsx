// AC1 — Catálogo de documentos: columnas, filas, paginación server-side y acciones.
//
// Uso de ejemplo:
//   render(<DocumentTypeListTable items={items} totalCount={40} page={1} pageSize={20} ... />);
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DocumentTypeListTable } from "../DocumentTypeListTable";
import type { DocumentType } from "@/lib/api/types-documents";

const items: DocumentType[] = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    codigo: "CEDULA",
    nombre: "Cédula de ciudadanía",
    descripcion: "Documento de identidad",
    estado: "activo",
    fechaCreacion: "2026-01-15T10:00:00Z",
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    codigo: "SOAT",
    nombre: "SOAT",
    estado: "inactivo",
    fechaCreacion: "2026-02-20T10:00:00Z",
    esAutogenerado: true,
  },
];

describe("DocumentTypeListTable (AC1)", () => {
  it("renderiza columnas y filas", () => {
    render(
      <DocumentTypeListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onEdit={vi.fn()}
        onDeactivate={vi.fn()}
        onReactivate={vi.fn()}
      />,
    );

    expect(screen.getByText("Código")).toBeInTheDocument();
    expect(screen.getByText("Nombre")).toBeInTheDocument();
    expect(screen.getByText("Origen")).toBeInTheDocument();
    expect(screen.getByText("CEDULA")).toBeInTheDocument();
    expect(screen.getByText("Cédula de ciudadanía")).toBeInTheDocument();
    expect(screen.getByText("Cargue")).toBeInTheDocument();
    expect(screen.getByText("Autogenerado")).toBeInTheDocument();
    expect(screen.getByText("Activo")).toBeInTheDocument();
    expect(screen.getByText("Inactivo")).toBeInTheDocument();
  });

  it("dispara onEdit y onDeactivate con el documento", () => {
    const onEdit = vi.fn();
    const onDeactivate = vi.fn();
    render(
      <DocumentTypeListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onEdit={onEdit}
        onDeactivate={onDeactivate}
        onReactivate={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /editar cédula/i }));
    expect(onEdit).toHaveBeenCalledWith(items[0]);

    fireEvent.click(screen.getByRole("switch", { name: /desactivar cédula/i }));
    expect(onDeactivate).toHaveBeenCalledWith(items[0]);
  });

  it("ofrece 'Activar' (no 'Desactivar') en documentos inactivos y dispara onReactivate", () => {
    const onReactivate = vi.fn();
    render(
      <DocumentTypeListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={vi.fn()}
        onEdit={vi.fn()}
        onDeactivate={vi.fn()}
        onReactivate={onReactivate}
      />,
    );

    // El inactivo (SOAT) no muestra 'Desactivar' sino 'Activar'.
    expect(screen.queryByRole("switch", { name: /desactivar soat/i })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("switch", { name: /activar soat/i }));
    expect(onReactivate).toHaveBeenCalledWith(items[1]);
  });

  it("pagina server-side vía onPageChange", () => {
    const onPageChange = vi.fn();
    render(
      <DocumentTypeListTable
        items={items}
        totalCount={40}
        page={1}
        pageSize={20}
        onPageChange={onPageChange}
        onEdit={vi.fn()}
        onDeactivate={vi.fn()}
        onReactivate={vi.fn()}
      />,
    );
    expect(screen.getByText("1 / 2")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /página siguiente/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});

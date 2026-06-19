// AC5 — Matriz resuelta: columna nivelAplicado (badge DEFAULT/OT/CLIENTE) y orden
// final tal como llega del API.
//
// Uso de ejemplo:
//   render(<ResolvedMatrixTable rows={rows} />);
import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ResolvedMatrixTable } from "../panels/ResolvedMatrixTable";
import type { ResolvedDocumentMatrixRow } from "@/lib/api/types-documents";

const rows: ResolvedDocumentMatrixRow[] = [
  { documentTypeId: "a", codigo: "DOCA", nombre: "Documento A", obligatorio: true, ordenResuelto: 1, nivelAplicado: "CLIENTE" },
  { documentTypeId: "b", codigo: "DOCB", nombre: "Documento B", obligatorio: false, ordenResuelto: 5, nivelAplicado: "OT" },
  { documentTypeId: "c", codigo: "DOCC", nombre: "Documento C", obligatorio: true, ordenResuelto: 9, nivelAplicado: "DEFAULT" },
];

describe("ResolvedMatrixTable (AC5)", () => {
  it("renderiza la columna nivelAplicado con el badge correcto por fila", () => {
    render(<ResolvedMatrixTable rows={rows} />);

    expect(screen.getByText("Nivel aplicado")).toBeInTheDocument();

    const rowA = screen.getByText("Documento A").closest("tr")!;
    expect(within(rowA).getByText("CLIENTE")).toBeInTheDocument();

    const rowB = screen.getByText("Documento B").closest("tr")!;
    expect(within(rowB).getByText("OT")).toBeInTheDocument();

    const rowC = screen.getByText("Documento C").closest("tr")!;
    expect(within(rowC).getByText("DEFAULT")).toBeInTheDocument();
  });

  it("muestra el orden resuelto y la obligatoriedad", () => {
    render(<ResolvedMatrixTable rows={rows} />);
    const rowA = screen.getByText("Documento A").closest("tr")!;
    expect(within(rowA).getByText("1")).toBeInTheDocument();
    expect(within(rowA).getByText("Sí")).toBeInTheDocument();
  });
});

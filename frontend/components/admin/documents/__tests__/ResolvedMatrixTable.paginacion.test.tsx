// HU #13918 — la matriz resuelta usa la tabla estándar con paginación en cliente.
import { beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ResolvedMatrixTable } from "../panels/ResolvedMatrixTable";
import type { ResolvedDocumentMatrixRow } from "@/lib/api/types-documents";

const rows: ResolvedDocumentMatrixRow[] = Array.from({ length: 11 }, (_, i) => ({
  documentTypeId: `d-${i + 1}`,
  codigo: `DOC${i + 1}`,
  nombre: `Documento ${i + 1}`,
  obligatorio: i % 2 === 0,
  ordenResuelto: i + 1,
  nivelAplicado: "DEFAULT",
}));

describe("ResolvedMatrixTable — paginación estándar (HU #13918)", () => {
  beforeEach(() => sessionStorage.clear());

  it("con 11 filas muestra 10 y «Última página» lleva a la última", () => {
    render(<ResolvedMatrixTable rows={rows} />);
    expect(screen.getByText("Documento 10")).toBeInTheDocument();
    expect(screen.queryByText("Documento 11")).not.toBeInTheDocument();
    expect(screen.getByText(/Mostrando 1–10 de 11/)).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /filas por página/i })).toHaveValue("10");

    fireEvent.click(screen.getByRole("button", { name: /última página/i }));
    expect(screen.getByText("Documento 11")).toBeInTheDocument();
    expect(screen.queryByText("Documento 1")).not.toBeInTheDocument();
    expect(screen.getByText(/Mostrando 11–11 de 11/)).toBeInTheDocument();
  });
});

// Modal emergente 409 (documento en uso): muestra los trámites y se cierra solo por
// acción del usuario (Entendido / X), no se autodescarta como el toast.
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DocumentInUseDialog } from "../DocumentInUseDialog";

const procedureTypes = [
  { codigo: "TRASPASO", nombre: "Traspaso" },
  { codigo: "MATRICULA_NUEVA", nombre: "Matricula inicial" },
];

describe("DocumentInUseDialog", () => {
  it("muestra el documento y la lista de trámites donde está en uso", () => {
    render(
      <DocumentInUseDialog documentName="Cedula" procedureTypes={procedureTypes} onClose={vi.fn()} />,
    );

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/Cedula/)).toBeInTheDocument();
    expect(screen.getByText("Traspaso")).toBeInTheDocument();
    expect(screen.getByText("Matricula inicial")).toBeInTheDocument();
    expect(screen.getByText("MATRICULA_NUEVA")).toBeInTheDocument();
  });

  it("se cierra con «Entendido» y con la X (no se autodescarta)", () => {
    const onClose = vi.fn();
    render(
      <DocumentInUseDialog documentName="Cedula" procedureTypes={procedureTypes} onClose={onClose} />,
    );

    fireEvent.click(screen.getByRole("button", { name: /entendido/i }));
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole("button", { name: /cerrar/i }));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("usa un texto genérico cuando no llega la lista de trámites", () => {
    render(<DocumentInUseDialog documentName="Cedula" procedureTypes={[]} onClose={vi.fn()} />);
    expect(screen.getByText(/uno o más tipos de trámite/i)).toBeInTheDocument();
  });
});

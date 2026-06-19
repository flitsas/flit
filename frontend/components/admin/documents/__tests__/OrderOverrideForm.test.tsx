// AC3/AC4 — Formulario de override: badge OT/CLIENTE visible, nota de precedencia
// para CLIENTE y envío del documento + orden.
//
// Uso de ejemplo:
//   render(<OrderOverrideForm scope="OT" documents={docs} onSubmit={fn} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OrderOverrideForm } from "../panels/OrderOverrideForm";
import type { DocumentType } from "@/lib/api/types-documents";

const documents: DocumentType[] = [
  { id: "doc-a", codigo: "DOCA", nombre: "Documento A", estado: "activo", fechaCreacion: "2026-01-01T00:00:00Z" },
  { id: "doc-b", codigo: "DOCB", nombre: "Documento B", estado: "activo", fechaCreacion: "2026-01-01T00:00:00Z" },
];

describe("OrderOverrideForm (AC3/AC4)", () => {
  it("muestra el badge OT y no la nota de precedencia", () => {
    render(<OrderOverrideForm scope="OT" documents={documents} onSubmit={vi.fn()} />);
    expect(screen.getByText("OT")).toBeInTheDocument();
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("muestra el badge CLIENTE y la nota de precedencia", () => {
    render(<OrderOverrideForm scope="CLIENTE" documents={documents} onSubmit={vi.fn()} />);
    expect(screen.getByText("CLIENTE")).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent(/CLIENTE > OT > Default/i);
  });

  it("envía el documento y el orden seleccionados", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<OrderOverrideForm scope="OT" documents={documents} onSubmit={onSubmit} />);

    await user.selectOptions(screen.getByLabelText(/documento/i), "doc-b");
    await user.type(screen.getByLabelText(/orden/i), "5");
    await user.click(screen.getByRole("button", { name: /agregar/i }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith("doc-b", 5));
  });

  it("valida que se elija un documento antes de enviar", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<OrderOverrideForm scope="OT" documents={documents} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText(/orden/i), "3");
    await user.click(screen.getByRole("button", { name: /agregar/i }));

    // El placeholder del <select> también contiene "Selecciona un documento…",
    // así que se valida el mensaje de error por su role="alert" (sin ambigüedad).
    expect(await screen.findByRole("alert")).toHaveTextContent(/selecciona un documento/i);
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

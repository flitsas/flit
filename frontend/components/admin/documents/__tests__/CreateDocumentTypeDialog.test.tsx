// AC1 — Alta/edición de tipo de documento: valida requeridos en cliente, envía el
// payload correcto y mapea los errores 422 del backend por campo (inline).
//
// Uso de ejemplo:
//   render(<CreateDocumentTypeDialog open onClose={fn} onSubmit={fn} onSaved={fn} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CreateDocumentTypeDialog } from "../CreateDocumentTypeDialog";
import { ApiValidationError } from "@/lib/api/types";
import type { DocumentType } from "@/lib/api/types-documents";

const saved: DocumentType = {
  id: "00000000-0000-0000-0000-000000000009",
  codigo: "RUNT",
  nombre: "Consulta RUNT",
  estado: "activo",
  fechaCreacion: "2026-06-19T00:00:00Z",
};

describe("CreateDocumentTypeDialog (AC1)", () => {
  it("valida requeridos en cliente y no llama a la API", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<CreateDocumentTypeDialog open onClose={vi.fn()} onSubmit={onSubmit} onSaved={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: /crear documento/i }));

    expect(await screen.findByText(/el código es obligatorio/i)).toBeInTheDocument();
    expect(screen.getByText(/el nombre es obligatorio/i)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("envía el payload y notifica onSaved en éxito", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(saved);
    const onSaved = vi.fn();
    render(<CreateDocumentTypeDialog open onClose={vi.fn()} onSubmit={onSubmit} onSaved={onSaved} />);

    await user.type(screen.getByLabelText(/código/i), "RUNT");
    await user.type(screen.getByLabelText(/nombre/i), "Consulta RUNT");
    await user.click(screen.getByRole("button", { name: /crear documento/i }));

    await waitFor(() =>
      // RF08/09 — sin formatos ni tamaño configurados ⇒ null (el backend aplica los globales).
      expect(onSubmit).toHaveBeenCalledWith({
        codigo: "RUNT",
        nombre: "Consulta RUNT",
        descripcion: null,
        mimeTypesAllowed: null,
        maxSizeBytes: null,
      }),
    );
    expect(onSaved).toHaveBeenCalledWith(saved, "create");
  });

  it("mapea el error 422 del backend al campo y no cierra el modal", async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    const onSubmit = vi
      .fn()
      .mockRejectedValue(new ApiValidationError([{ field: "codigo", message: "Ya existe un documento con ese código." }], 422));

    render(<CreateDocumentTypeDialog open onClose={vi.fn()} onSubmit={onSubmit} onSaved={onSaved} />);

    await user.type(screen.getByLabelText(/código/i), "DUP");
    await user.type(screen.getByLabelText(/nombre/i), "Duplicado");
    await user.click(screen.getByRole("button", { name: /crear documento/i }));

    expect(await screen.findByText(/ya existe un documento con ese código/i)).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("precarga los valores en modo edición", () => {
    render(
      <CreateDocumentTypeDialog open editing={saved} onClose={vi.fn()} onSubmit={vi.fn()} onSaved={vi.fn()} />,
    );
    expect(screen.getByLabelText(/código/i)).toHaveValue("RUNT");
    expect(screen.getByLabelText(/nombre/i)).toHaveValue("Consulta RUNT");
    expect(screen.getByRole("button", { name: /guardar cambios/i })).toBeInTheDocument();
  });
});

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

    expect(await screen.findByText(/el nombre es obligatorio/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^código$/i)).not.toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("envía el payload sin código y notifica onSaved en éxito", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(saved);
    const onSaved = vi.fn();
    render(<CreateDocumentTypeDialog open onClose={vi.fn()} onSubmit={onSubmit} onSaved={onSaved} />);

    await user.type(screen.getByLabelText(/nombre/i), "Consulta RUNT");
    await user.click(screen.getByRole("button", { name: /crear documento/i }));

    await waitFor(() =>
      expect(onSubmit).toHaveBeenCalledWith({
        nombre: "Consulta RUNT",
        descripcion: null,
        mimeTypesAllowed: null,
        maxSizeBytes: null,
        esAutogenerado: false,
      }),
    );
    expect(onSaved).toHaveBeenCalledWith(saved, "create");
  });

  it("mapea el error 422 del backend y no cierra el modal", async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    const onSubmit = vi
      .fn()
      .mockRejectedValue(new ApiValidationError([{ field: "nombre", message: "El nombre es obligatorio." }], 422));

    render(<CreateDocumentTypeDialog open onClose={vi.fn()} onSubmit={onSubmit} onSaved={onSaved} />);

    await user.type(screen.getByLabelText(/nombre/i), "Duplicado");
    await user.click(screen.getByRole("button", { name: /crear documento/i }));

    expect(await screen.findByText(/el nombre es obligatorio/i)).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("en edición muestra el código de sistema en solo lectura", async () => {
    const user = userEvent.setup();
    render(
      <CreateDocumentTypeDialog open editing={saved} onClose={vi.fn()} onSubmit={vi.fn()} onSaved={vi.fn()} />,
    );
    const code = screen.getByLabelText(/^código$/i);
    expect(code).toHaveValue("RUNT");
    expect(code).toHaveAttribute("readonly");
    await user.type(code, "HACK");
    expect(code).toHaveValue("RUNT");
    expect(screen.getByLabelText(/nombre/i)).toHaveValue("Consulta RUNT");
    expect(screen.getByRole("button", { name: /guardar cambios/i })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /cargue/i })).toBeChecked();
  });

  it("envía esAutogenerado true cuando se elige Autogenerado", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(saved);
    render(<CreateDocumentTypeDialog open onClose={vi.fn()} onSubmit={onSubmit} onSaved={vi.fn()} />);

    await user.type(screen.getByLabelText(/^nombre$/i), "Consulta RUNT");
    await user.click(screen.getByRole("radio", { name: /autogenerado/i }));
    await user.click(screen.getByRole("button", { name: /crear documento/i }));

    await waitFor(() =>
      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ esAutogenerado: true }),
      ),
    );
  });
});

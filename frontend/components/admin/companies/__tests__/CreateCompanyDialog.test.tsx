// Alta de compañía (#10118) — el modal valida requeridos en cliente, envía el
// payload correcto vía onCreate y mapea los errores 422 del backend por campo.
// HU #13607 — el formulario ya no pide el código: lo asigna el servidor (COMP-XXXX).
//
// Uso de ejemplo:
//   render(<CreateCompanyDialog open onClose={fn} onCreate={fn} onCreated={fn} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CreateCompanyDialog } from "../CreateCompanyDialog";
import { ApiValidationError } from "@/lib/api/types";

const createdCompany = {
  id: "00000000-0000-0000-0000-000000000009",
  nit: "900123456-1",
  razonSocial: "Renting Andino S.A.S.",
  estadoActivo: true,
  fechaCreacion: "2026-06-19T00:00:00Z",
};

describe("CreateCompanyDialog (#10118)", () => {
  it("valida requeridos en cliente y no llama a la API", async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn();
    render(<CreateCompanyDialog open onClose={vi.fn()} onCreate={onCreate} onCreated={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    expect(await screen.findByText(/la razón social es obligatoria/i)).toBeInTheDocument();
    expect(screen.getByText(/el nit es obligatorio/i)).toBeInTheDocument();
    expect(onCreate).not.toHaveBeenCalled();
  });

  it("envía el payload correcto y notifica onCreated en éxito", async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn().mockResolvedValue(createdCompany);
    const onCreated = vi.fn();

    render(<CreateCompanyDialog open onClose={vi.fn()} onCreate={onCreate} onCreated={onCreated} />);

    await user.type(screen.getByLabelText(/razón social/i), "Renting Andino S.A.S.");
    await user.type(screen.getByLabelText(/^nit$/i), "900123456-1");
    await user.selectOptions(screen.getByLabelText(/tipo de compañía/i), "CONCESIONARIO");
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    await waitFor(() =>
      expect(onCreate).toHaveBeenCalledWith({
        razonSocial: "Renting Andino S.A.S.",
        nit: "900123456-1",
        tenantType: "CONCESIONARIO",
        estadoActivo: true,
      }),
    );
    expect(onCreated).toHaveBeenCalledWith(createdCompany);
  });

  it("mapea el error 422 del backend al campo y no cierra el modal", async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    const onCreate = vi
      .fn()
      .mockRejectedValue(
        new ApiValidationError([{ field: "nit", message: "Ya existe una compañía con ese NIT." }], 422),
      );

    render(<CreateCompanyDialog open onClose={vi.fn()} onCreate={onCreate} onCreated={onCreated} />);

    await user.type(screen.getByLabelText(/razón social/i), "Otra Compañía");
    await user.type(screen.getByLabelText(/^nit$/i), "900222333-4");
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    expect(await screen.findByText(/ya existe una compañía con ese nit/i)).toBeInTheDocument();
    expect(onCreated).not.toHaveBeenCalled();
  });

  it("HU #13607: no muestra el campo Código ni lo envía", async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn().mockResolvedValue({ ...createdCompany, code: "COMP-0577" });
    render(<CreateCompanyDialog open onClose={vi.fn()} onCreate={onCreate} onCreated={vi.fn()} />);

    expect(screen.queryByLabelText(/código/i)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/razón social/i), "Renting Andino S.A.S.");
    await user.type(screen.getByLabelText(/^nit$/i), "900123456-1");
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    await waitFor(() => expect(onCreate).toHaveBeenCalled());
    expect(onCreate.mock.calls[0][0]).not.toHaveProperty("code");
  });

  it("HU #13607: un error sin campo se muestra como alerta y no cierra el modal", async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    const onCreate = vi.fn().mockRejectedValue(new Error("red caída"));
    render(<CreateCompanyDialog open onClose={vi.fn()} onCreate={onCreate} onCreated={onCreated} />);

    await user.type(screen.getByLabelText(/razón social/i), "Renting Andino S.A.S.");
    await user.type(screen.getByLabelText(/^nit$/i), "900123456-1");
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo crear la compañía/i);
    expect(onCreated).not.toHaveBeenCalled();
  });
});

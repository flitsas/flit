// HU #10622 — Modal "Editar usuario": formulario prellenado (AC1), mapeo de error de
// correo en uso sin perder lo escrito (AC2) y aviso de conflicto de concurrencia (AC3).
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EditUserModal } from "../EditUserModal";
import { ApiError } from "@/lib/api/types";

const user = {
  id: "user-1",
  fullName: "Laura García",
  email: "laura@flit.local",
  rowVersion: 3,
};

describe("EditUserModal (#10622)", () => {
  it("AC1: precarga el formulario con nombre y correo del usuario, con foco inicial en nombre", () => {
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={vi.fn()} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    expect(nameInput).toHaveValue("Laura García");
    expect(screen.getByLabelText(/correo electrónico/i)).toHaveValue("laura@flit.local");
    expect(nameInput).toHaveFocus();
  });

  it("envía displayName/email recortados y rowVersion, y notifica onSaved en éxito", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi.fn().mockResolvedValue(undefined);
    const onSaved = vi.fn();
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={onSaved} onUpdate={onUpdate} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    await ue.clear(nameInput);
    await ue.type(nameInput, "Laura García Ruiz");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(onUpdate).toHaveBeenCalledWith("user-1", {
        displayName: "Laura García Ruiz",
        email: "laura@flit.local",
        rowVersion: 3,
      }),
    );
    expect(onSaved).toHaveBeenCalledTimes(1);
  });

  it("AC2: mapea el 409 USER_ALREADY_EXISTS sin perder lo escrito en el formulario", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "USER_ALREADY_EXISTS" }));
    const onSaved = vi.fn();
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={onSaved} onUpdate={onUpdate} />);

    const emailInput = screen.getByLabelText(/correo electrónico/i);
    await ue.clear(emailInput);
    await ue.type(emailInput, "otro@flit.local");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ese correo ya está en uso/i);
    // No se pierde lo escrito.
    expect(screen.getByLabelText(/correo electrónico/i)).toHaveValue("otro@flit.local");
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("AC2: mapea el 409 EMAIL_BELONGS_TO_DELETED_USER", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "EMAIL_BELONGS_TO_DELETED_USER" }));
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={onUpdate} />);

    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /pertenece a una cuenta eliminada/i,
    );
  });

  it("AC3: aviso de conflicto de concurrencia (CONCURRENCY_CONFLICT), formulario se mantiene abierto", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "CONCURRENCY_CONFLICT" }));
    const onClose = vi.fn();
    const onSaved = vi.fn();
    render(<EditUserModal user={user} onClose={onClose} onSaved={onSaved} onUpdate={onUpdate} />);

    const nameInput = screen.getByLabelText(/nombre completo/i);
    await ue.clear(nameInput);
    await ue.type(nameInput, "Nombre editado sin guardar");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /modificado por otra persona.*cierra el diálogo/i,
    );
    // El modal no se cierra solo ni pierde lo escrito.
    expect(onClose).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nombre completo/i)).toHaveValue("Nombre editado sin guardar");
  });

  it("mapea el 404 (usuario ya no existe)", async () => {
    const ue = userEvent.setup();
    const onUpdate = vi.fn().mockRejectedValue(new ApiError(404, "Not found"));
    render(<EditUserModal user={user} onClose={vi.fn()} onSaved={vi.fn()} onUpdate={onUpdate} />);

    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/el usuario ya no existe/i);
  });

  it("cierra el modal al presionar Escape (sin operaciones en curso)", async () => {
    const ue = userEvent.setup();
    const onClose = vi.fn();
    render(<EditUserModal user={user} onClose={onClose} onSaved={vi.fn()} onUpdate={vi.fn()} />);

    await ue.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

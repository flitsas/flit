// HU #10623 (AC3) — Diálogo "Restaurar usuario", exclusivo de SuperAdmin. Un clic de
// confirmación deshace el soft-delete. Mapea USER_NOT_DELETED (AC5) como defensa en profundidad.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RestoreUserDialog } from "../RestoreUserDialog";
import { ApiError } from "@/lib/api/types";

const user = {
  id: "user-1",
  fullName: "Laura García",
  email: "laura@flit.local",
};

describe("RestoreUserDialog (#10623)", () => {
  it("confirma la restauración llamando a onRestore con el userId", async () => {
    const ue = userEvent.setup();
    const onRestore = vi.fn().mockResolvedValue(undefined);
    const onRestored = vi.fn();
    render(
      <RestoreUserDialog
        user={user}
        onClose={vi.fn()}
        onRestored={onRestored}
        onRestore={onRestore}
      />,
    );

    await ue.click(screen.getByRole("button", { name: /^restaurar usuario$/i }));

    await waitFor(() => expect(onRestore).toHaveBeenCalledWith("user-1"));
    expect(onRestored).toHaveBeenCalledTimes(1);
  });

  it("mapea USER_NOT_DELETED", async () => {
    const ue = userEvent.setup();
    const onRestore = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "USER_NOT_DELETED" }));
    render(
      <RestoreUserDialog
        user={user}
        onClose={vi.fn()}
        onRestored={vi.fn()}
        onRestore={onRestore}
      />,
    );

    await ue.click(screen.getByRole("button", { name: /^restaurar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no está eliminado/i);
  });

  it("cierra el diálogo al presionar Cancelar", async () => {
    const ue = userEvent.setup();
    const onClose = vi.fn();
    render(
      <RestoreUserDialog user={user} onClose={onClose} onRestored={vi.fn()} onRestore={vi.fn()} />,
    );

    await ue.click(screen.getByRole("button", { name: /cancelar/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

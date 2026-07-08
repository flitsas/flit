// HU #10623 — Diálogo "Eliminar usuario". AC1: la confirmación deja explícito que solo un
// SuperAdmin puede restaurar. Mapea SELF_DELETE/LAST_ACTIVE_ADMIN/CONCURRENCY_CONFLICT
// (defensa en profundidad — el botón no debería mostrarse en esos casos, ver AC2).
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DeleteUserDialog } from "../DeleteUserDialog";
import { ApiError } from "@/lib/api/types";

const user = {
  id: "user-1",
  fullName: "Laura García",
  email: "laura@flit.local",
  rowVersion: 3,
};

describe("DeleteUserDialog (#10623)", () => {
  it("AC1: la confirmación aclara que solo un Super Admin puede restaurar al usuario", () => {
    render(
      <DeleteUserDialog user={user} onClose={vi.fn()} onDeleted={vi.fn()} onDelete={vi.fn()} />,
    );

    expect(screen.getByText(/laura garcía/i)).toBeInTheDocument();
    // El texto está partido por un <strong>, se busca por el textContent del párrafo completo.
    expect(
      screen.getByText(
        (_, node) => node?.tagName === "P" && /solo un.*super admin.*puede restaurarlo/i.test(node.textContent ?? ""),
      ),
    ).toBeInTheDocument();
  });

  it("confirma la eliminación llamando a onDelete con el userId y rowVersion leídos", async () => {
    const ue = userEvent.setup();
    const onDelete = vi.fn().mockResolvedValue(undefined);
    const onDeleted = vi.fn();
    render(
      <DeleteUserDialog user={user} onClose={vi.fn()} onDeleted={onDeleted} onDelete={onDelete} />,
    );

    await ue.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    await waitFor(() => expect(onDelete).toHaveBeenCalledWith("user-1", 3));
    expect(onDeleted).toHaveBeenCalledTimes(1);
  });

  it("mapea SELF_DELETE (defensa en profundidad, campo `code`)", async () => {
    const ue = userEvent.setup();
    const onDelete = vi
      .fn()
      .mockRejectedValue(new ApiError(400, "Bad Request", { code: "SELF_DELETE" }));
    render(
      <DeleteUserDialog user={user} onClose={vi.fn()} onDeleted={vi.fn()} onDelete={onDelete} />,
    );

    await ue.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no puedes eliminarte a ti mismo/i);
  });

  it("mapea SELF_DELETE cuando el backend usa el campo `error` (AdminOtEndpoints)", async () => {
    const ue = userEvent.setup();
    const onDelete = vi
      .fn()
      .mockRejectedValue(new ApiError(400, "Bad Request", { error: "SELF_DELETE" }));
    render(
      <DeleteUserDialog user={user} onClose={vi.fn()} onDeleted={vi.fn()} onDelete={onDelete} />,
    );

    await ue.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no puedes eliminarte a ti mismo/i);
  });

  it("mapea LAST_ACTIVE_ADMIN", async () => {
    const ue = userEvent.setup();
    const onDelete = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "LAST_ACTIVE_ADMIN" }));
    render(
      <DeleteUserDialog user={user} onClose={vi.fn()} onDeleted={vi.fn()} onDelete={onDelete} />,
    );

    await ue.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/último administrador activo/i);
  });

  it("mapea CONCURRENCY_CONFLICT y no cierra el diálogo", async () => {
    const ue = userEvent.setup();
    const onDelete = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "CONCURRENCY_CONFLICT" }));
    const onClose = vi.fn();
    const onDeleted = vi.fn();
    render(
      <DeleteUserDialog user={user} onClose={onClose} onDeleted={onDeleted} onDelete={onDelete} />,
    );

    await ue.click(screen.getByRole("button", { name: /^eliminar usuario$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/modificado por otra persona/i);
    expect(onClose).not.toHaveBeenCalled();
    expect(onDeleted).not.toHaveBeenCalled();
  });

  it("cierra el diálogo al presionar Cancelar", async () => {
    const ue = userEvent.setup();
    const onClose = vi.fn();
    render(
      <DeleteUserDialog user={user} onClose={onClose} onDeleted={vi.fn()} onDelete={vi.fn()} />,
    );

    await ue.click(screen.getByRole("button", { name: /cancelar/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

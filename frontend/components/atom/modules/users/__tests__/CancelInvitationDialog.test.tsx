// HU #10628 — Diálogo "Cancelar invitación". AC1: confirmación exitosa. AC2: texto explícito de
// que la acción es distinta de "Eliminar usuario". AC3: mapea 404/409 (condición de carrera) y
// dispara `onStale` para que el padre refresque el listado sin cerrar el diálogo todavía.
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CancelInvitationDialog } from "../CancelInvitationDialog";
import { ApiError } from "@/lib/api/types";

const invitation = {
  id: "invitation-1",
  fullName: "Carlos Ruiz",
  email: "carlos@flit.local",
};

describe("CancelInvitationDialog (#10628)", () => {
  it("AC2: la confirmación aclara que la acción es distinta de 'Eliminar usuario'", () => {
    render(
      <CancelInvitationDialog
        invitation={invitation}
        onClose={vi.fn()}
        onCancelled={vi.fn()}
        onStale={vi.fn()}
        onCancel={vi.fn()}
      />,
    );

    expect(screen.getByText(/carlos@flit\.local/i)).toBeInTheDocument();
    // El texto está partido por un <strong>, se busca por el textContent del párrafo completo.
    expect(
      screen.getByText(
        (_, node) => node?.tagName === "P" && /distinta de.*eliminar usuario/i.test(node.textContent ?? ""),
      ),
    ).toBeInTheDocument();
  });

  it("AC1: confirma la cancelación llamando a onCancel con el invitationId y avisa a onCancelled", async () => {
    const ue = userEvent.setup();
    const onCancel = vi.fn().mockResolvedValue(undefined);
    const onCancelled = vi.fn();
    render(
      <CancelInvitationDialog
        invitation={invitation}
        onClose={vi.fn()}
        onCancelled={onCancelled}
        onStale={vi.fn()}
        onCancel={onCancel}
      />,
    );

    await ue.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    await waitFor(() => expect(onCancel).toHaveBeenCalledWith("invitation-1"));
    expect(onCancelled).toHaveBeenCalledTimes(1);
  });

  it("AC3: mapea 404 (la invitación ya no existe) y dispara onStale sin cerrar el diálogo", async () => {
    const ue = userEvent.setup();
    const onCancel = vi
      .fn()
      .mockRejectedValue(new ApiError(404, "Not Found", { code: "INVITATION_NOT_FOUND" }));
    const onStale = vi.fn();
    const onCancelled = vi.fn();
    const onClose = vi.fn();
    render(
      <CancelInvitationDialog
        invitation={invitation}
        onClose={onClose}
        onCancelled={onCancelled}
        onStale={onStale}
        onCancel={onCancel}
      />,
    );

    await ue.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no existe/i);
    expect(onStale).toHaveBeenCalledTimes(1);
    expect(onCancelled).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("AC3: mapea 409 (la invitación ya no está pendiente, campo `error` de OT) y dispara onStale", async () => {
    const ue = userEvent.setup();
    const onCancel = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { error: "INVITATION_NOT_PENDING" }));
    const onStale = vi.fn();
    render(
      <CancelInvitationDialog
        invitation={invitation}
        onClose={vi.fn()}
        onCancelled={vi.fn()}
        onStale={onStale}
        onCancel={onCancel}
      />,
    );

    await ue.click(screen.getByRole("button", { name: /^cancelar invitación$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/ya no está pendiente/i);
    expect(onStale).toHaveBeenCalledTimes(1);
  });

  it("cierra el diálogo al presionar Volver", async () => {
    const ue = userEvent.setup();
    const onClose = vi.fn();
    render(
      <CancelInvitationDialog
        invitation={invitation}
        onClose={onClose}
        onCancelled={vi.fn()}
        onStale={vi.fn()}
        onCancel={vi.fn()}
      />,
    );

    await ue.click(screen.getByRole("button", { name: /volver/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

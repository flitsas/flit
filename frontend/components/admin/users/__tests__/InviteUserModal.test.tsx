import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createInvitation } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { InviteUserModal } from "../InviteUserModal";

vi.mock("@/lib/api/security", () => ({ createInvitation: vi.fn() }));
const inviteMock = vi.mocked(createInvitation);

const noop = () => {};

describe("InviteUserModal (HU #10178)", () => {
  beforeEach(() => vi.clearAllMocks());

  // AC1 — formulario completo → invitación creada → muestra confirmación
  it("AC1: envía invitación y muestra confirmación", async () => {
    inviteMock.mockResolvedValue({
      invitationId: "abc-123",
      email: "nuevo@empresa.com",
      emailSent: true,
    });
    const onSuccess = vi.fn();
    render(<InviteUserModal onClose={noop} onSuccess={onSuccess} />);

    fireEvent.change(screen.getByLabelText(/correo electrónico/i), {
      target: { value: "nuevo@empresa.com" },
    });
    fireEvent.change(screen.getByLabelText(/id de rol/i), {
      target: { value: "role-uuid-1234" },
    });
    fireEvent.click(screen.getByRole("button", { name: /enviar invitación/i }));

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(/invitación enviada/i),
    );
    expect(inviteMock).toHaveBeenCalledWith("nuevo@empresa.com", "role-uuid-1234");
    expect(onSuccess).toHaveBeenCalledWith("nuevo@empresa.com");
  });

  // AC2 — API responde 409 → muestra error sin cerrar el modal
  it("AC2: error 409 muestra mensaje accesible y no cierra el modal", async () => {
    inviteMock.mockRejectedValue(new ApiError(409, "INVITATION_ALREADY_PENDING"));
    const onClose = vi.fn();
    render(<InviteUserModal onClose={onClose} onSuccess={noop} />);

    fireEvent.change(screen.getByLabelText(/correo electrónico/i), {
      target: { value: "existente@empresa.com" },
    });
    fireEvent.change(screen.getByLabelText(/id de rol/i), {
      target: { value: "role-uuid-1234" },
    });
    fireEvent.click(screen.getByRole("button", { name: /enviar invitación/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/invitación pendiente/i);
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/correo electrónico/i)).toBeInTheDocument();
  });

  // emailSent=false → muestra aviso amber sin bloquear cierre
  it("muestra aviso cuando el email no pudo enviarse", async () => {
    inviteMock.mockResolvedValue({
      invitationId: "abc-456",
      email: "sin-email@empresa.com",
      emailSent: false,
    });
    render(<InviteUserModal onClose={noop} onSuccess={noop} />);

    fireEvent.change(screen.getByLabelText(/correo electrónico/i), {
      target: { value: "sin-email@empresa.com" },
    });
    fireEvent.change(screen.getByLabelText(/id de rol/i), {
      target: { value: "role-uuid-1234" },
    });
    fireEvent.click(screen.getByRole("button", { name: /enviar invitación/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/correo de activación no pudo enviarse/i);
  });
});

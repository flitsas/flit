// HU #11552 / ADR-0048 — Botón "Reactivar invitación" (espejo de ResendInvitationButton).
// Cubre: happy path, cooldown 429, los tres códigos de conflicto de correo (409), el 409
// INVITATION_NOT_CANCELLED, accesibilidad (nombre accesible + estado deshabilitado anunciado +
// mensaje con role="alert"/aria-live).
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ReactivateInvitationButton } from "../ReactivateInvitationButton";
import { ApiError } from "@/lib/api/types";

describe("ReactivateInvitationButton", () => {
  it("uso de ejemplo: reactivate exitoso muestra confirmación y deshabilita el botón", async () => {
    const reactivate = vi.fn().mockResolvedValue({ email: "ana@flit.local", emailSent: true });
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-1" fullName="Ana Ruiz" reactivate={reactivate} />,
    );

    const button = screen.getByRole("button", { name: "Reactivar invitación a Ana Ruiz" });
    await user.click(button);

    expect(reactivate).toHaveBeenCalledWith("inv-1");
    expect(await screen.findByText(/invitación reactivada y reenviada a ana@flit\.local/i)).toBeInTheDocument();
    // `disabled` nativo — anunciado automáticamente por lectores de pantalla (mismo patrón que
    // ResendInvitationButton, sin necesidad de aria-disabled adicional).
    expect(button).toBeDisabled();
  });

  it("nombre accesible cuando el correo no pudo entregarse", async () => {
    const reactivate = vi.fn().mockResolvedValue({ email: "beto@flit.local", emailSent: false });
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-2" fullName="Beto Ríos" reactivate={reactivate} />,
    );

    await user.click(screen.getByRole("button", { name: "Reactivar invitación a Beto Ríos" }));

    expect(
      await screen.findByText(/reactivada, pero el correo no pudo entregarse a beto@flit\.local/i),
    ).toBeInTheDocument();
  });

  it("409 INVITATION_ALREADY_PENDING usa el literal unificado de conflicto de correo", async () => {
    const reactivate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "INVITATION_ALREADY_PENDING" }));
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-3" fullName="Caro Díaz" reactivate={reactivate} />,
    );

    await user.click(screen.getByRole("button", { name: "Reactivar invitación a Caro Díaz" }));

    const message = await screen.findByRole("alert");
    expect(message).toHaveTextContent("El correo utilizado ya se encuentra asociado a otra cuenta");
    expect(message).toHaveAttribute("aria-live", "assertive");
  });

  it("409 EMAIL_BELONGS_TO_DELETED_USER (campo 'error', ruta OT) usa el mismo literal unificado", async () => {
    const reactivate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { error: "EMAIL_BELONGS_TO_DELETED_USER" }));
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-4" fullName="Dani Soto" reactivate={reactivate} />,
    );

    await user.click(screen.getByRole("button", { name: "Reactivar invitación a Dani Soto" }));

    expect(
      await screen.findByText(/el correo utilizado ya se encuentra asociado a otra cuenta/i),
    ).toBeInTheDocument();
  });

  it("409 INVITATION_NOT_CANCELLED muestra un mensaje propio, distinto del conflicto de correo", async () => {
    const reactivate = vi
      .fn()
      .mockRejectedValue(new ApiError(409, "Conflict", { code: "INVITATION_NOT_CANCELLED" }));
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-5" fullName="Eva Lima" reactivate={reactivate} />,
    );

    await user.click(screen.getByRole("button", { name: "Reactivar invitación a Eva Lima" }));

    expect(
      await screen.findByText(/la invitación ya no está cancelada/i),
    ).toBeInTheDocument();
  });

  it("429 reemplaza el cooldown optimista por el retryAfterSeconds real y actualiza el aria-label", async () => {
    const reactivate = vi.fn().mockRejectedValue(
      new ApiError(429, "Too Many Requests", {
        code: "RESEND_COOLDOWN_ACTIVE",
        message: "Debes esperar 45 segundos antes de reactivar esta invitación de nuevo.",
        retryAfterSeconds: 45,
      }),
    );
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-6" fullName="Fer Ríos" reactivate={reactivate} />,
    );

    const button = screen.getByRole("button", { name: "Reactivar invitación a Fer Ríos" });
    await user.click(button);

    expect(
      await screen.findByText(/debes esperar 45 segundos antes de reactivar esta invitación de nuevo/i),
    ).toBeInTheDocument();
    expect(button).toBeDisabled();
    expect(
      screen.getByRole("button", { name: /reactivar invitación a fer ríos \(disponible en 45 segundos\)/i }),
    ).toBeInTheDocument();
  });

  it("error genérico (no 409/429) muestra un mensaje de reintento", async () => {
    const reactivate = vi.fn().mockRejectedValue(new Error("network down"));
    const user = userEvent.setup();
    render(
      <ReactivateInvitationButton invitationId="inv-7" fullName="Gina Paz" reactivate={reactivate} />,
    );

    await user.click(screen.getByRole("button", { name: "Reactivar invitación a Gina Paz" }));

    expect(
      await screen.findByText(/no se pudo reactivar la invitación\. inténtalo de nuevo/i),
    ).toBeInTheDocument();
  });
});

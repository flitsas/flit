import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { forgotPassword } from "@/lib/api/auth";
import { ForgotPasswordForm } from "../ForgotPasswordForm";

vi.mock("@/lib/api/auth", () => ({ forgotPassword: vi.fn() }));
const forgotMock = vi.mocked(forgotPassword);

describe("ForgotPasswordForm (HU #10173)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("envía la solicitud y muestra confirmación genérica", async () => {
    forgotMock.mockResolvedValue(undefined);
    render(<ForgotPasswordForm />);
    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "demo@flit.local" } });
    fireEvent.click(screen.getByRole("button", { name: /enviar/i }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent(/si el correo está registrado/i));
    expect(forgotMock).toHaveBeenCalledWith("demo@flit.local");
  });

  it("muestra el mismo mensaje genérico aunque la API falle (anti-enumeración)", async () => {
    forgotMock.mockRejectedValue({ status: 500 });
    render(<ForgotPasswordForm />);
    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "x@y.z" } });
    fireEvent.click(screen.getByRole("button", { name: /enviar/i }));

    expect(await screen.findByRole("status")).toHaveTextContent(/si el correo está registrado/i);
  });

  it("valida correo vacío sin llamar a la API", () => {
    render(<ForgotPasswordForm />);
    fireEvent.click(screen.getByRole("button", { name: /enviar/i }));
    expect(screen.getByRole("alert")).toHaveTextContent(/ingresa tu correo/i);
    expect(forgotMock).not.toHaveBeenCalled();
  });
});

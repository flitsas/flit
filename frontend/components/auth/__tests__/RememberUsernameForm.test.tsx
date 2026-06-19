import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { rememberUsername } from "@/lib/api/auth";
import { RememberUsernameForm } from "../RememberUsernameForm";

vi.mock("@/lib/api/auth", () => ({ rememberUsername: vi.fn() }));
const rememberMock = vi.mocked(rememberUsername);

describe("RememberUsernameForm (HU #10204)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("AC1 — identificador válido → confirmación genérica", async () => {
    rememberMock.mockResolvedValue(undefined);
    render(<RememberUsernameForm />);

    fireEvent.change(screen.getByLabelText(/número de documento/i), { target: { value: "1020304050" } });
    fireEvent.click(screen.getByRole("button", { name: /recordar mi usuario/i }));

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(/si el documento corresponde a una cuenta/i),
    );
    expect(rememberMock).toHaveBeenCalledWith("1020304050");
  });

  it("AC2 — documento vacío → error de validación sin enviar", () => {
    render(<RememberUsernameForm />);
    fireEvent.click(screen.getByRole("button", { name: /recordar mi usuario/i }));

    expect(screen.getByRole("alert")).toHaveTextContent(/ingresa tu número de documento/i);
    expect(rememberMock).not.toHaveBeenCalled();
  });

  it("AC2 — formato inválido (no numérico) → error sin enviar", () => {
    render(<RememberUsernameForm />);
    fireEvent.change(screen.getByLabelText(/número de documento/i), { target: { value: "abc-12" } });
    fireEvent.click(screen.getByRole("button", { name: /recordar mi usuario/i }));

    expect(screen.getByRole("alert")).toHaveTextContent(/solo números/i);
    expect(rememberMock).not.toHaveBeenCalled();
  });
});

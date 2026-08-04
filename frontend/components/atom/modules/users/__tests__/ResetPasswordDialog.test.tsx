import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResetPasswordDialog } from "../ResetPasswordDialog";
import { adminResetPassword } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/types";

vi.mock("@/lib/api/auth", () => ({
  adminResetPassword: vi.fn(),
}));

vi.mock("@/components/atom/Modal", () => ({
  Modal: ({
    children,
    title,
  }: {
    children: React.ReactNode;
    title: string;
    open: boolean;
    onClose: () => void;
    size?: string;
  }) => (
    <div role="dialog" aria-label={title}>
      <h2>{title}</h2>
      {children}
    </div>
  ),
}));

const resetMock = vi.mocked(adminResetPassword);

describe("ResetPasswordDialog (HU-B)", () => {
  beforeEach(() => {
    resetMock.mockReset();
  });

  it("confirma el reset y muestra mensaje de cambio obligatorio", async () => {
    resetMock.mockResolvedValueOnce(undefined);
    const onDone = vi.fn();
    const user = userEvent.setup();

    render(
      <ResetPasswordDialog
        user={{ fullName: "Ana Pérez", email: "ana@empresa.local" }}
        onClose={vi.fn()}
        onDone={onDone}
      />,
    );

    await user.click(screen.getByRole("button", { name: /confirmar reset/i }));

    await waitFor(() => expect(resetMock).toHaveBeenCalledWith("ana@empresa.local"));
    expect(onDone).toHaveBeenCalled();
    expect(
      screen.getByText(/deberá definir una nueva en su próximo inicio de sesión/i),
    ).toBeInTheDocument();
  });

  it("muestra acceso restringido en 403 sin aplicar éxito", async () => {
    resetMock.mockRejectedValueOnce(new ApiError(403, "fuera"));
    const user = userEvent.setup();

    render(
      <ResetPasswordDialog
        user={{ fullName: "Ana Pérez", email: "ana@empresa.local" }}
        onClose={vi.fn()}
        onDone={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("button", { name: /confirmar reset/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/acceso restringido/i);
    expect(screen.queryByText(/se restableció la contraseña/i)).not.toBeInTheDocument();
  });
});

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { adminResetPassword } from "@/lib/api/auth";
import { ResetPasswordModal } from "../ResetPasswordModal";

vi.mock("@/lib/api/auth", () => ({ adminResetPassword: vi.fn() }));
const resetMock = vi.mocked(adminResetPassword);

describe("ResetPasswordModal (HU #10174 AC1)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("confirma el reset e informa el cambio obligatorio en el próximo acceso", async () => {
    resetMock.mockResolvedValue(undefined);
    render(<ResetPasswordModal email="demo@flit.local" onClose={() => {}} />);

    fireEvent.click(screen.getByRole("button", { name: /confirmar reset/i }));

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(/próximo inicio de sesión/i),
    );
    expect(resetMock).toHaveBeenCalledWith("demo@flit.local");
  });

  it("muestra error 403 (fuera de ámbito) sin confirmar", async () => {
    resetMock.mockRejectedValue({ status: 403 });
    render(<ResetPasswordModal email="otro@flit.local" onClose={() => {}} />);

    fireEvent.click(screen.getByRole("button", { name: /confirmar reset/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/acceso restringido/i);
    expect(screen.queryByRole("status")).toBeNull();
  });
});

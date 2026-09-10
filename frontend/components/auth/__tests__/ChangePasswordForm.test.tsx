import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { changePassword } from "@/lib/api/auth";
import { ChangePasswordForm } from "../ChangePasswordForm";

vi.mock("@/lib/api/auth", () => ({ changePassword: vi.fn() }));
const changeMock = vi.mocked(changePassword);

function fill(current: string, next: string, confirm: string) {
  fireEvent.change(screen.getByLabelText("Contraseña actual"), { target: { value: current } });
  fireEvent.change(screen.getByLabelText("Nueva contraseña"), { target: { value: next } });
  fireEvent.change(screen.getByLabelText("Confirmar nueva contraseña"), { target: { value: confirm } });
}

describe("ChangePasswordForm (HU #10173 / RF24)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("cambia la contraseña con datos válidos", async () => {
    changeMock.mockResolvedValue(undefined);
    render(<ChangePasswordForm />);
    fill("DemoPass1!", "NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /cambiar/i }));

    await waitFor(() => expect(screen.getByRole("status")).toBeInTheDocument());
    expect(changeMock).toHaveBeenCalledWith("DemoPass1!", "NewPass123");
  });

  it("rechaza nueva contraseña que no cumple política", () => {
    render(<ChangePasswordForm />);
    fill("DemoPass1!", "weak", "weak");
    fireEvent.click(screen.getByRole("button", { name: /cambiar/i }));
    expect(screen.getByRole("alert")).toHaveTextContent(/mínimo 8/i);
    expect(changeMock).not.toHaveBeenCalled();
  });

  it("muestra error si la contraseña actual es incorrecta (400)", async () => {
    changeMock.mockRejectedValue({ status: 400 });
    render(<ChangePasswordForm />);
    fill("WrongCurrent", "NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /cambiar/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/actual es incorrecta/i);
  });

  // HU #11553 AC1 — el backend rechaza fijar la misma contraseña con 409 PASSWORD_REUSED.
  it("muestra mensaje explicativo si la nueva contraseña es igual a la actual (409 PASSWORD_REUSED)", async () => {
    changeMock.mockRejectedValue({ status: 409, body: { code: "PASSWORD_REUSED", message: "..." } });
    render(<ChangePasswordForm />);
    fill("DemoPass1!", "DemoPass1!", "DemoPass1!");
    fireEvent.click(screen.getByRole("button", { name: /cambiar/i }));
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/diferente a la actual/i);
  });

  it("un 409 con otro código no muestra el mensaje de reutilización", async () => {
    changeMock.mockRejectedValue({ status: 409, body: { code: "OTHER_CONFLICT" } });
    render(<ChangePasswordForm />);
    fill("DemoPass1!", "NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /cambiar/i }));
    const alert = await screen.findByRole("alert");
    expect(alert).not.toHaveTextContent(/diferente a la actual/i);
    expect(alert).toHaveTextContent(/no se pudo cambiar/i);
  });
});
